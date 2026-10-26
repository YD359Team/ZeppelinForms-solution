using System.Buffers.Binary;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>
/// JPEG into RGBA: baseline and progressive, Huffman-coded, 8 bits a sample, any sampling
/// factors, restart intervals; grayscale, YCbCr, RGB, and Adobe's CMYK and YCCK.
/// </summary>
/// <remarks>
/// <para>
/// The coefficients of every block are kept until the last scan — a progressive picture
/// refines them scan by scan — then dequantized and transformed. The chroma is upsampled
/// by the triangle filter libjpeg's "fancy" upsampling is, so a picture comes out as it
/// does from Skia, which decodes with libjpeg-turbo.
/// </para>
/// <para>
/// A picture much larger than the size it is wanted at is decoded at a half, a quarter or
/// an eighth straight from its coefficients — libjpeg's scaled decoding — instead of in
/// full and then scaled down: a twelve-megapixel photo for a 2048-pixel limit is decoded
/// at half its size, in a quarter of the time and the memory.
/// </para>
/// <para>
/// Arithmetic coding, 12-bit samples and the lossless process are refused with a message
/// that says so; they are rare outside medical imaging. The EXIF orientation is not
/// applied, as Skia's backend doesn't apply it either.
/// </para>
/// </remarks>
internal sealed class JpegDecoder
{
    // the zigzag order of the coefficients in the stream, as their natural indices
    private static readonly byte[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        // a run past the end lands on a harmless last coefficient
        63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    ];

    // the YCbCr conversion of libjpeg, in 16-bit fixed point: its tables to the bit
    private static readonly int[] CrToR = new int[256], CbToB = new int[256];
    private static readonly int[] CrToG = new int[256], CbToG = new int[256];

    private readonly byte[] _data;
    private int _position;

    private readonly ushort[][] _quantization = new ushort[4][];
    private readonly Huffman?[] _dcTables = new Huffman?[4];
    private readonly Huffman?[] _acTables = new Huffman?[4];

    private Component[] _components = [];
    private int _width, _height;

    /// <summary>The picture's size as it is put out: the frame's, divided by the scale.</summary>
    private int _outWidth, _outHeight;

    /// <summary>The samples a block is transformed into across: 8 at full size, 4, 2 or 1
    /// when the picture is decoded scaled down.</summary>
    private int _blockSize = 8;

    private readonly int _maxDimension;
    private int _maxH, _maxV;
    private int _mcusPerLine, _mcusPerColumn;
    private bool _progressive;
    private int _restartInterval;

    private int _adobeTransform = -1;
    private bool _jfif;

    // the bit reader of the entropy-coded segment
    private uint _bits;
    private int _bitCount;
    private bool _atMarker;
    private int _eobRun;

    private JpegDecoder(byte[] data, int maxDimension)
    {
        _data = data;
        _maxDimension = maxDimension;
    }

    static JpegDecoder()
    {
        const int Half = 1 << 15;

        for (int i = 0; i < 256; i++)
        {
            int x = i - 128;

            CrToR[i] = (Fixed(1.40200) * x + Half) >> 16;
            CbToB[i] = (Fixed(1.77200) * x + Half) >> 16;
            CrToG[i] = -Fixed(0.71414) * x;
            CbToG[i] = -Fixed(0.34414) * x + Half;
        }

        static int Fixed(double value) => (int)(value * 65536 + 0.5);
    }

    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <param name="maxDimension">The largest size either way the picture is wanted at:
    /// a picture at least twice as large is decoded at a half, a quarter or an eighth,
    /// still no smaller than this. 0 — always in full.</param>
    public static (int Width, int Height, byte[] Pixels) Decode(byte[] data, int maxDimension = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsJpeg(data)) throw new InvalidDataException("Not a JPEG file.");

        return new JpegDecoder(data, maxDimension).Run();
    }

    private sealed class Component
    {
        public int Id;
        public int H, V;
        public int Quantization;
        public int DcTable, AcTable;
        public int Predictor;

        /// <summary>The blocks across and down, padded to whole MCUs.</summary>
        public int BlocksPerLine, BlocksPerColumn;

        /// <summary>The samples across and down the picture really has.</summary>
        public int SampleWidth, SampleHeight;

        public short[] Coefficients = [];
    }

    #region Markers

    private (int, int, byte[]) Run()
    {
        _position = 2;
        bool frame = false;
        bool scanned = false;

        while (true)
        {
            int marker = NextMarker();

            if (marker < 0 || marker == 0xD9) break; // the end of the file, or of the picture

            switch (marker)
            {
                case 0xC0: // baseline
                case 0xC1: // extended sequential, Huffman
                case 0xC2: // progressive, Huffman
                    ReadFrame(Segment(), progressive: marker == 0xC2);
                    frame = true;
                    break;

                case 0xC3:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xCB:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    throw new InvalidDataException("Lossless and hierarchical JPEG pictures are not decoded by the GLES backend.");

                case 0xC9:
                case 0xCA:
                    throw new InvalidDataException("Arithmetic-coded JPEG pictures are not decoded by the GLES backend.");

                case 0xC4:
                    ReadHuffmanTables(Segment());
                    break;

                case 0xDB:
                    ReadQuantizationTables(Segment());
                    break;

                case 0xDD:
                    {
                        ReadOnlySpan<byte> segment = Segment();
                        _restartInterval = segment.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(segment) : 0;
                        break;
                    }

                case 0xDA:
                    if (!frame) throw new InvalidDataException("A JPEG scan comes before its frame header.");

                    ReadScan(Segment());
                    scanned = true;
                    break;

                case 0xE0:
                    {
                        ReadOnlySpan<byte> segment = Segment();
                        if (segment.StartsWith("JFIF\0"u8)) _jfif = true;
                        break;
                    }

                case 0xEE:
                    {
                        ReadOnlySpan<byte> segment = Segment();
                        if (segment.Length >= 12 && segment.StartsWith("Adobe"u8)) _adobeTransform = segment[11];
                        break;
                    }

                default:
                    // a marker without a segment stands alone; every other one is skipped
                    if (marker is not (>= 0xD0 and <= 0xD7) and not 0x01) Segment();
                    break;
            }
        }

        if (!frame || !scanned) throw new InvalidDataException("The JPEG file has no picture in it.");

        ChooseScale();

        byte[] pixels = Output();
        return (_outWidth, _outHeight, pixels);
    }

    /// <summary>The next marker's code, past fill bytes; −1 at the end of the data.</summary>
    private int NextMarker()
    {
        while (_position + 1 < _data.Length)
        {
            if (_data[_position] != 0xFF)
            {
                // stray bytes between segments: skipped, as libjpeg does with a warning
                _position++;
                continue;
            }

            int code = _data[_position + 1];

            if (code == 0xFF)
            {
                _position++;
                continue;
            }

            _position += 2;

            if (code == 0x00) continue;

            return code;
        }

        return -1;
    }

    /// <summary>The segment after a marker, without its length.</summary>
    private ReadOnlySpan<byte> Segment()
    {
        if (_position + 2 > _data.Length) throw new InvalidDataException("A JPEG segment runs past the end of the file.");

        int length = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_position));

        if (length < 2 || _position + length > _data.Length)
            throw new InvalidDataException("A JPEG segment runs past the end of the file.");

        ReadOnlySpan<byte> segment = _data.AsSpan(_position + 2, length - 2);
        _position += length;

        return segment;
    }

    private void ReadFrame(ReadOnlySpan<byte> segment, bool progressive)
    {
        if (_components.Length > 0) throw new InvalidDataException("The JPEG file has more than one frame.");
        if (segment.Length < 6) throw new InvalidDataException("The JPEG frame header is too short.");

        if (segment[0] != 8)
            throw new InvalidDataException($"{segment[0]}-bit JPEG pictures are not decoded by the GLES backend: 8 bits are.");

        _progressive = progressive;
        _height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        _width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);

        int count = segment[5];

        if (_width == 0 || _height == 0) throw new InvalidDataException("The JPEG picture has no size.");
        if (count is not (1 or 3 or 4)) throw new InvalidDataException($"JPEG pictures of {count} components are not decoded.");
        if (segment.Length < 6 + count * 3) throw new InvalidDataException("The JPEG frame header is too short.");

        _components = new Component[count];

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> c = segment.Slice(6 + i * 3, 3);

            int h = c[1] >> 4, v = c[1] & 15;
            if (h is < 1 or > 4 || v is < 1 or > 4) throw new InvalidDataException("A JPEG component's sampling is out of range.");

            _components[i] = new Component { Id = c[0], H = h, V = v, Quantization = c[2] & 3 };
        }

        _maxH = _components.Max(static c => c.H);
        _maxV = _components.Max(static c => c.V);

        _mcusPerLine = (_width + 8 * _maxH - 1) / (8 * _maxH);
        _mcusPerColumn = (_height + 8 * _maxV - 1) / (8 * _maxV);

        foreach (Component c in _components)
        {
            c.BlocksPerLine = _mcusPerLine * c.H;
            c.BlocksPerColumn = _mcusPerColumn * c.V;
            c.SampleWidth = (_width * c.H + _maxH - 1) / _maxH;
            c.SampleHeight = (_height * c.V + _maxV - 1) / _maxV;
            c.Coefficients = new short[c.BlocksPerLine * c.BlocksPerColumn * 64];
        }
    }

    private void ReadQuantizationTables(ReadOnlySpan<byte> segment)
    {
        int p = 0;

        while (p < segment.Length)
        {
            int precision = segment[p] >> 4;
            int id = segment[p] & 3;
            p++;

            var table = new ushort[64];

            for (int k = 0; k < 64; k++)
            {
                if (precision == 0)
                {
                    if (p >= segment.Length) throw new InvalidDataException("A JPEG quantization table is cut short.");
                    table[ZigZag[k]] = segment[p++];
                }
                else
                {
                    if (p + 1 >= segment.Length) throw new InvalidDataException("A JPEG quantization table is cut short.");
                    table[ZigZag[k]] = BinaryPrimitives.ReadUInt16BigEndian(segment[p..]);
                    p += 2;
                }
            }

            _quantization[id] = table;
        }
    }

    private void ReadHuffmanTables(ReadOnlySpan<byte> segment)
    {
        int p = 0;

        while (p + 17 <= segment.Length)
        {
            int kind = segment[p] >> 4;
            int id = segment[p] & 3;

            ReadOnlySpan<byte> counts = segment.Slice(p + 1, 16);
            int total = 0;
            foreach (byte n in counts) total += n;

            p += 17;
            if (p + total > segment.Length) throw new InvalidDataException("A JPEG Huffman table is cut short.");

            var table = new Huffman(counts, segment.Slice(p, total));
            p += total;

            if (kind == 0) _dcTables[id] = table;
            else _acTables[id] = table;
        }
    }

    #endregion

    #region Scans

    private void ReadScan(ReadOnlySpan<byte> segment)
    {
        int count = segment.Length > 0 ? segment[0] : 0;
        if (count < 1 || segment.Length < 1 + count * 2 + 3) throw new InvalidDataException("The JPEG scan header is too short.");

        var scan = new Component[count];

        for (int i = 0; i < count; i++)
        {
            int id = segment[1 + i * 2];
            int tables = segment[2 + i * 2];

            Component c = Array.Find(_components, x => x.Id == id)
                ?? throw new InvalidDataException("A JPEG scan names a component the frame hasn't got.");

            c.DcTable = tables >> 4;
            c.AcTable = tables & 15;
            scan[i] = c;
        }

        int start = segment[1 + count * 2];
        int end = segment[2 + count * 2];
        int approximation = segment[3 + count * 2];
        int high = approximation >> 4, low = approximation & 15;

        if (!_progressive)
        {
            start = 0;
            end = 63;
            high = low = 0;
        }

        if (start > end || end > 63) throw new InvalidDataException("A JPEG scan's spectral selection is out of range.");

        DecodeScan(scan, start, end, high, low);
    }

    private void DecodeScan(Component[] scan, int start, int end, int high, int low)
    {
        ResetBits();

        foreach (Component c in scan) c.Predictor = 0;
        _eobRun = 0;

        bool single = scan.Length == 1;

        // a scan of one component goes block by block over just the blocks it has;
        // an interleaved one MCU by MCU
        int unitsAcross, unitsDown;

        if (single)
        {
            Component c = scan[0];
            unitsAcross = (c.SampleWidth + 7) / 8;
            unitsDown = (c.SampleHeight + 7) / 8;
        }
        else
        {
            unitsAcross = _mcusPerLine;
            unitsDown = _mcusPerColumn;
        }

        int total = unitsAcross * unitsDown;
        int sinceRestart = 0;

        for (int unit = 0; unit < total; unit++)
        {
            if (_restartInterval > 0 && sinceRestart == _restartInterval)
            {
                Restart(scan);
                sinceRestart = 0;
            }

            int ux = unit % unitsAcross, uy = unit / unitsAcross;

            if (single)
            {
                DecodeBlock(scan[0], uy, ux, start, end, high, low);
            }
            else
            {
                foreach (Component c in scan)
                {
                    for (int v = 0; v < c.V; v++)
                    {
                        for (int h = 0; h < c.H; h++)
                            DecodeBlock(c, uy * c.V + v, ux * c.H + h, start, end, high, low);
                    }
                }
            }

            sinceRestart++;
        }

        // past the scan's last bits, to the next marker
        if (_atMarker) return;

        while (_position + 1 < _data.Length && !(_data[_position] == 0xFF && _data[_position + 1] is not 0x00 and not (>= 0xD0 and <= 0xD7)))
            _position++;
    }

    /// <summary>A restart marker: the bits start over at a byte, the predictions at zero.</summary>
    private void Restart(Component[] scan)
    {
        // to the marker: the bits before it are padding
        while (_position + 1 < _data.Length && !(_data[_position] == 0xFF && _data[_position + 1] is >= 0xD0 and <= 0xD7))
        {
            // a marker of another kind: the data is cut short; what follows decodes as zeros
            if (_data[_position] == 0xFF && _data[_position + 1] is not 0x00 and not 0xFF) break;

            _position++;
        }

        if (_position + 1 < _data.Length && _data[_position] == 0xFF && _data[_position + 1] is >= 0xD0 and <= 0xD7)
            _position += 2;

        ResetBits();

        foreach (Component c in scan) c.Predictor = 0;
        _eobRun = 0;
    }

    private void DecodeBlock(Component c, int row, int column, int start, int end, int high, int low)
    {
        // blocks beyond the padded grid: a stream that claims more than the frame
        if (row >= c.BlocksPerColumn || column >= c.BlocksPerLine) return;

        Span<short> block = c.Coefficients.AsSpan((row * c.BlocksPerLine + column) * 64, 64);

        if (!_progressive)
        {
            DecodeBaseline(c, block);
        }
        else if (start == 0)
        {
            if (high == 0) DecodeDcFirst(c, block, low);
            else DecodeDcRefine(block, low);
        }
        else
        {
            if (high == 0) DecodeAcFirst(c, block, start, end, low);
            else DecodeAcRefine(c, block, start, end, low);
        }
    }

    private void DecodeBaseline(Component c, Span<short> block)
    {
        Huffman dc = _dcTables[c.DcTable] ?? throw new InvalidDataException("A JPEG scan uses a DC table it doesn't define.");
        Huffman ac = _acTables[c.AcTable] ?? throw new InvalidDataException("A JPEG scan uses an AC table it doesn't define.");

        int t = DecodeHuffman(dc);
        int diff = t == 0 ? 0 : Extend(ReceiveBits(t), t);

        c.Predictor += diff;
        block[0] = (short)c.Predictor;

        int k = 1;

        while (k < 64)
        {
            int rs = DecodeHuffman(ac);
            int s = rs & 15, r = rs >> 4;

            if (s == 0)
            {
                if (r != 15) break;

                k += 16;
                continue;
            }

            k += r;
            if (k > 63) break;

            block[ZigZag[k]] = (short)Extend(ReceiveBits(s), s);
            k++;
        }
    }

    private void DecodeDcFirst(Component c, Span<short> block, int low)
    {
        Huffman dc = _dcTables[c.DcTable] ?? throw new InvalidDataException("A JPEG scan uses a DC table it doesn't define.");

        int t = DecodeHuffman(dc);
        int diff = t == 0 ? 0 : Extend(ReceiveBits(t), t);

        c.Predictor += diff;
        block[0] = (short)(c.Predictor * (1 << low));
    }

    private void DecodeDcRefine(Span<short> block, int low)
    {
        if (ReceiveBits(1) != 0) block[0] |= (short)(1 << low);
    }

    private void DecodeAcFirst(Component c, Span<short> block, int start, int end, int low)
    {
        if (_eobRun > 0)
        {
            _eobRun--;
            return;
        }

        Huffman ac = _acTables[c.AcTable] ?? throw new InvalidDataException("A JPEG scan uses an AC table it doesn't define.");

        int k = start;

        while (k <= end)
        {
            int rs = DecodeHuffman(ac);
            int s = rs & 15, r = rs >> 4;

            if (s == 0)
            {
                if (r < 15)
                {
                    _eobRun = (1 << r) - 1;
                    if (r > 0) _eobRun += ReceiveBits(r);
                    break;
                }

                k += 16;
                continue;
            }

            k += r;
            if (k > 63) break;

            block[ZigZag[k]] = (short)(Extend(ReceiveBits(s), s) * (1 << low));
            k++;
        }
    }

    /// <summary>A refinement of the AC coefficients by one bit: the new ones of magnitude
    /// one placed among the zeros, a correction bit for every one already there — the
    /// procedure of G.1.2.3, as libjpeg and stb_image have it.</summary>
    private void DecodeAcRefine(Component c, Span<short> block, int start, int end, int low)
    {
        short bit = (short)(1 << low);
        int k = start;

        if (_eobRun == 0)
        {
            Huffman ac = _acTables[c.AcTable] ?? throw new InvalidDataException("A JPEG scan uses an AC table it doesn't define.");

            while (k <= end)
            {
                int rs = DecodeHuffman(ac);
                int s = rs & 15, r = rs >> 4;
                short value = 0;

                if (s == 0)
                {
                    if (r < 15)
                    {
                        // the end of the band in this block and in the next ones: the rest of
                        // this one only gets its correction bits
                        _eobRun = 1 << r;
                        if (r > 0) _eobRun += ReceiveBits(r);
                        break;
                    }

                    // sixteen zeros skipped, the nonzero ones among them corrected
                }
                else
                {
                    value = ReceiveBits(1) != 0 ? bit : (short)-bit;
                }

                while (k <= end)
                {
                    ref short coefficient = ref block[ZigZag[k++]];

                    if (coefficient != 0)
                    {
                        Correct(ref coefficient);
                    }
                    else
                    {
                        if (r == 0)
                        {
                            coefficient = value;
                            break;
                        }

                        r--;
                    }
                }
            }
        }

        if (_eobRun > 0)
        {
            // within an end-of-band run: only the correction bits of what is there
            for (; k <= end; k++)
            {
                ref short coefficient = ref block[ZigZag[k]];
                if (coefficient != 0) Correct(ref coefficient);
            }

            _eobRun--;
        }

        void Correct(ref short coefficient)
        {
            if (ReceiveBits(1) != 0 && (coefficient & bit) == 0)
                coefficient += coefficient > 0 ? bit : (short)-bit;
        }
    }

    #endregion

    #region Bits

    private void ResetBits()
    {
        _bits = 0;
        _bitCount = 0;
        _atMarker = false;
    }

    /// <summary>At least 25 bits in the buffer: the stuffed zero after an 0xFF skipped, a
    /// marker not consumed — past it the data reads as zeros, as libjpeg pads a cut stream.</summary>
    private void Fill()
    {
        while (_bitCount <= 24)
        {
            uint b = 0;

            if (!_atMarker && _position < _data.Length)
            {
                b = _data[_position];

                if (b == 0xFF)
                {
                    int next = _position + 1 < _data.Length ? _data[_position + 1] : 0xD9;

                    if (next == 0x00)
                    {
                        _position += 2;
                    }
                    else
                    {
                        _atMarker = true;
                        b = 0;
                    }
                }
                else
                {
                    _position++;
                }
            }

            _bits |= b << (24 - _bitCount);
            _bitCount += 8;
        }
    }

    private int ReceiveBits(int count)
    {
        if (count == 0) return 0;

        Fill();

        int value = (int)(_bits >> (32 - count));
        _bits <<= count;
        _bitCount -= count;

        return value;
    }

    /// <summary>A received value of <paramref name="size"/> bits as the signed number it codes.</summary>
    private static int Extend(int value, int size) =>
        value < 1 << (size - 1) ? value - (1 << size) + 1 : value;

    private int DecodeHuffman(Huffman table)
    {
        Fill();

        int peek = (int)(_bits >> (32 - Huffman.FastBits));
        ushort fast = table.Fast[peek];

        if (fast != 0)
        {
            int length = fast >> 8;
            _bits <<= length;
            _bitCount -= length;
            return fast & 0xFF;
        }

        // a code longer than the fast table: compared length by length
        int code = 0;

        for (int length = 1; length <= 16; length++)
        {
            code = (code << 1) | (int)(_bits >> 31);
            _bits <<= 1;
            _bitCount--;

            if (_bitCount < 8) Fill();

            if (code <= table.MaxCode[length])
                return table.Values[table.ValueOffset[length] + code - table.MinCode[length]];
        }

        throw new InvalidDataException("The JPEG data has a Huffman code its table doesn't define.");
    }

    /// <summary>A Huffman table: a lookup of the codes up to nine bits long, and the
    /// canonical bounds of every length for the longer ones.</summary>
    private sealed class Huffman
    {
        public const int FastBits = 9;

        /// <summary>The length in the high byte, the symbol in the low one; 0 — longer.</summary>
        public readonly ushort[] Fast = new ushort[1 << FastBits];

        public readonly int[] MinCode = new int[17];
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValueOffset = new int[17];
        public readonly byte[] Values;

        public Huffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            Values = values.ToArray();

            int code = 0;
            int index = 0;

            for (int length = 1; length <= 16; length++)
            {
                int n = counts[length - 1];

                ValueOffset[length] = index;
                MinCode[length] = code;
                MaxCode[length] = n > 0 ? code + n - 1 : -1;

                for (int i = 0; i < n; i++, index++, code++)
                {
                    if (length > FastBits) continue;

                    // every peek that starts with this code
                    int shift = FastBits - length;
                    int first = code << shift;

                    for (int j = 0; j < 1 << shift; j++)
                        Fast[first + j] = (ushort)((length << 8) | Values[index]);
                }

                code <<= 1;
            }

            MaxCode[17] = int.MaxValue;
        }
    }

    #endregion

    #region Output

    /// <summary>The largest reduction that keeps the picture at least as large as it is
    /// wanted: what is left is scaled down by averaging, as a picture of another format is.</summary>
    private void ChooseScale()
    {
        _blockSize = 8;

        if (_maxDimension > 0)
        {
            double wanted = Math.Min((double)_maxDimension / _width, (double)_maxDimension / _height);

            while (_blockSize > 1 && (_blockSize / 2) / 8.0 >= wanted) _blockSize /= 2;
        }

        int n = _blockSize;

        _outWidth = (_width * n + 7) / 8;
        _outHeight = (_height * n + 7) / 8;

        foreach (Component c in _components)
        {
            c.SampleWidth = (_width * c.H * n + _maxH * 8 - 1) / (_maxH * 8);
            c.SampleHeight = (_height * c.V * n + _maxV * 8 - 1) / (_maxV * 8);
        }
    }

    private byte[] Output()
    {
        int count = _components.Length;
        var planes = new byte[count][];

        for (int i = 0; i < count; i++)
        {
            Component c = _components[i];
            ushort[] table = _quantization[c.Quantization] ?? throw new InvalidDataException("A JPEG component's quantization table is missing.");

            planes[i] = Upsample(c, Transform(c, table));
        }

        byte[] pixels = new byte[_outWidth * _outHeight * 4];
        int pixelCount = _outWidth * _outHeight;

        if (count == 1)
        {
            byte[] y = planes[0];

            for (int p = 0; p < pixelCount; p++)
            {
                byte v = y[p];
                pixels[p * 4] = v;
                pixels[p * 4 + 1] = v;
                pixels[p * 4 + 2] = v;
                pixels[p * 4 + 3] = 255;
            }

            return pixels;
        }

        // RGB when Adobe says so, or when the components are named R, G, B without JFIF
        bool rgb = count == 3 && (_adobeTransform == 0 ||
            (_adobeTransform < 0 && !_jfif && _components[0].Id == 'R' && _components[1].Id == 'G' && _components[2].Id == 'B'));

        // four components: YCCK when Adobe says so, else CMYK
        bool ycck = count == 4 && _adobeTransform == 2;

        byte[] p0 = planes[0], p1 = planes[1], p2 = planes[2];
        int[] crToR = CrToR, cbToB = CbToB, crToG = CrToG, cbToG = CbToG;

        if (count == 3 && !rgb)
        {
            // the usual case on its own, without a branch a pixel
            for (int p = 0, o = 0; p < pixelCount; p++, o += 4)
            {
                int y = p0[p], cb = p1[p], cr = p2[p];

                pixels[o] = Clamp(y + crToR[cr]);
                pixels[o + 1] = Clamp(y + ((cbToG[cb] + crToG[cr]) >> 16));
                pixels[o + 2] = Clamp(y + cbToB[cb]);
                pixels[o + 3] = 255;
            }

            return pixels;
        }

        byte[]? p3 = count == 4 ? planes[3] : null;

        for (int p = 0, o = 0; p < pixelCount; p++, o += 4)
        {
            int r = p0[p], g = p1[p], b = p2[p];

            if (ycck)
            {
                int y = r, cb = g, cr = b;

                // YCCK codes the inverted cyan, magenta and yellow
                r = 255 - Clamp(y + crToR[cr]);
                g = 255 - Clamp(y + ((cbToG[cb] + crToG[cr]) >> 16));
                b = 255 - Clamp(y + cbToB[cb]);
            }

            if (p3 is not null)
            {
                int k = p3[p];

                // Adobe writes CMYK inverted — a full ink is 0 — and so it is read, as
                // libjpeg-turbo's users and Skia read it
                r = r * k / 255;
                g = g * k / 255;
                b = b * k / 255;
            }

            pixels[o] = (byte)r;
            pixels[o + 1] = (byte)g;
            pixels[o + 2] = (byte)b;
            pixels[o + 3] = 255;
        }

        return pixels;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private static byte Clamp(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    /// <summary>The component's blocks dequantized and transformed into its samples, a
    /// plane of all its padded blocks, each block <see cref="_blockSize"/> samples across.</summary>
    private byte[] Transform(Component c, ushort[] table)
    {
        int n = _blockSize;
        int stride = c.BlocksPerLine * n;
        var plane = new byte[stride * c.BlocksPerColumn * n];

        Span<int> workspace = stackalloc int[64];
        Span<float> rows = stackalloc float[64];
        float[]? basis = n < 8 ? ScaledBasis(n) : null;

        for (int by = 0; by < c.BlocksPerColumn; by++)
        {
            for (int bx = 0; bx < c.BlocksPerLine; bx++)
            {
                ReadOnlySpan<short> block = c.Coefficients.AsSpan((by * c.BlocksPerLine + bx) * 64, 64);
                Span<byte> output = plane.AsSpan(by * n * stride + bx * n);

                if (basis is null) InverseDct(block, table, workspace, output, stride);
                else ScaledInverseDct(block, table, basis, n, rows, output, stride);
            }
        }

        return plane;
    }

    private static readonly float[]?[] ScaledBases = new float[]?[8];

    /// <summary>The basis of an N-point inverse DCT of a block's N × N lowest frequencies:
    /// cos((2x + 1)·u·π / 2N) · C(u) / 2, [x · N + u] — the samples it gives are the block's
    /// at 1/(8/N) of its size, at the same level.</summary>
    private static float[] ScaledBasis(int n)
    {
        lock (ScaledBases)
        {
            if (ScaledBases[n] is { } cached) return cached;

            var basis = new float[n * n];

            for (int x = 0; x < n; x++)
            {
                for (int u = 0; u < n; u++)
                {
                    double cu = u == 0 ? 1 / Math.Sqrt(2) : 1;
                    basis[x * n + u] = (float)(cu / 2 * Math.Cos((2 * x + 1) * u * Math.PI / (2 * n)));
                }
            }

            return ScaledBases[n] = basis;
        }
    }

    /// <summary>A block decoded at a reduced size: the N-point transform of its N × N
    /// lowest frequencies, the rest — finer than the samples can hold — dropped.</summary>
    private static void ScaledInverseDct(
        ReadOnlySpan<short> block, ushort[] q, float[] basis, int n, Span<float> rows, Span<byte> output, int stride)
    {
        // the rows of the low frequencies into the samples across
        for (int v = 0; v < n; v++)
        {
            for (int x = 0; x < n; x++)
            {
                float sum = 0;

                for (int u = 0; u < n; u++)
                    sum += basis[x * n + u] * block[v * 8 + u] * q[v * 8 + u];

                rows[v * n + x] = sum;
            }
        }

        // and down
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float sum = 0;

                for (int v = 0; v < n; v++) sum += basis[y * n + v] * rows[v * n + x];

                output[y * stride + x] = Clamp(sum + 128f);
            }
        }
    }

    // the constants of libjpeg's accurate integer IDCT, jidctint.c: 13 bits of fraction
    private const int ConstBits = 13, Pass1Bits = 2;
    private const int Fix0298631336 = 2446, Fix0390180644 = 3196, Fix0541196100 = 4433, Fix0765366865 = 6270;
    private const int Fix0899976223 = 7373, Fix1175875602 = 9633, Fix1501321110 = 12299, Fix1847759065 = 15137;
    private const int Fix1961570560 = 16069, Fix2053119869 = 16819, Fix2562915447 = 20995, Fix3072711026 = 25172;

    /// <summary>A block dequantized and transformed: libjpeg's "islow" inverse DCT — the
    /// one Skia's libjpeg-turbo decodes with — so the samples are the same to the bit.</summary>
    private static void InverseDct(ReadOnlySpan<short> block, ushort[] q, Span<int> ws, Span<byte> output, int stride)
    {
        // the columns, into the workspace, scaled up by Pass1Bits
        for (int col = 0; col < 8; col++)
        {
            if (block[8 + col] == 0 && block[16 + col] == 0 && block[24 + col] == 0 && block[32 + col] == 0 &&
                block[40 + col] == 0 && block[48 + col] == 0 && block[56 + col] == 0)
            {
                // a column of its DC only: flat
                int dc = block[col] * q[col] << Pass1Bits;
                for (int k = 0; k < 8; k++) ws[k * 8 + col] = dc;
                continue;
            }

            int z2 = block[16 + col] * q[16 + col];
            int z3 = block[48 + col] * q[48 + col];

            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 - z3 * Fix1847759065;
            int tmp3 = z1 + z2 * Fix0765366865;

            z2 = block[col] * q[col];
            z3 = block[32 + col] * q[32 + col];

            int tmp0 = (z2 + z3) << ConstBits;
            int tmp1 = (z2 - z3) << ConstBits;

            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            tmp0 = block[56 + col] * q[56 + col];
            tmp1 = block[40 + col] * q[40 + col];
            tmp2 = block[24 + col] * q[24 + col];
            tmp3 = block[8 + col] * q[8 + col];

            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int Shift = ConstBits - Pass1Bits;

            ws[col] = Descale(tmp10 + tmp3, Shift);
            ws[56 + col] = Descale(tmp10 - tmp3, Shift);
            ws[8 + col] = Descale(tmp11 + tmp2, Shift);
            ws[48 + col] = Descale(tmp11 - tmp2, Shift);
            ws[16 + col] = Descale(tmp12 + tmp1, Shift);
            ws[40 + col] = Descale(tmp12 - tmp1, Shift);
            ws[24 + col] = Descale(tmp13 + tmp0, Shift);
            ws[32 + col] = Descale(tmp13 - tmp0, Shift);
        }

        // the rows, into the samples, centered on 128
        for (int row = 0; row < 8; row++)
        {
            Span<int> w = ws.Slice(row * 8, 8);
            Span<byte> o = output.Slice(row * stride, 8);

            if (w[1] == 0 && w[2] == 0 && w[3] == 0 && w[4] == 0 && w[5] == 0 && w[6] == 0 && w[7] == 0)
            {
                o.Fill(Clamp(Descale(w[0], Pass1Bits + 3) + 128));
                continue;
            }

            int z2 = w[2], z3 = w[6];

            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 - z3 * Fix1847759065;
            int tmp3 = z1 + z2 * Fix0765366865;

            int tmp0 = (w[0] + w[4]) << ConstBits;
            int tmp1 = (w[0] - w[4]) << ConstBits;

            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            tmp0 = w[7];
            tmp1 = w[5];
            tmp2 = w[3];
            tmp3 = w[1];

            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int Shift = ConstBits + Pass1Bits + 3;

            o[0] = Clamp(Descale(tmp10 + tmp3, Shift) + 128);
            o[7] = Clamp(Descale(tmp10 - tmp3, Shift) + 128);
            o[1] = Clamp(Descale(tmp11 + tmp2, Shift) + 128);
            o[6] = Clamp(Descale(tmp11 - tmp2, Shift) + 128);
            o[2] = Clamp(Descale(tmp12 + tmp1, Shift) + 128);
            o[5] = Clamp(Descale(tmp12 - tmp1, Shift) + 128);
            o[3] = Clamp(Descale(tmp13 + tmp0, Shift) + 128);
            o[4] = Clamp(Descale(tmp13 - tmp0, Shift) + 128);
        }

        static int Descale(int x, int n) => (x + (1 << (n - 1))) >> n;

        // the odd part of the 8-point transform, in place: the four inputs 7, 5, 3, 1
        static void Odd(ref int tmp0, ref int tmp1, ref int tmp2, ref int tmp3)
        {
            int z1 = tmp0 + tmp3, z2 = tmp1 + tmp2, z3 = tmp0 + tmp2, z4 = tmp1 + tmp3;
            int z5 = (z3 + z4) * Fix1175875602;

            tmp0 *= Fix0298631336;
            tmp1 *= Fix2053119869;
            tmp2 *= Fix3072711026;
            tmp3 *= Fix1501321110;

            z1 *= -Fix0899976223;
            z2 *= -Fix2562915447;
            z3 = z3 * -Fix1961570560 + z5;
            z4 = z4 * -Fix0390180644 + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;
        }
    }

    /// <summary>A component's samples at the picture's size: as they are at full
    /// sampling, else by the triangle filter — the neighbours weighted by their distance to
    /// the pixel's center, three to one for a factor of two, as libjpeg's fancy upsampling.</summary>
    private byte[] Upsample(Component c, byte[] plane)
    {
        int stride = c.BlocksPerLine * _blockSize;
        int width = _outWidth, height = _outHeight;
        var output = new byte[width * height];

        if (c.H == _maxH && c.V == _maxV)
        {
            for (int y = 0; y < height; y++)
                plane.AsSpan(y * stride, width).CopyTo(output.AsSpan(y * width, width));

            return output;
        }

        if (c.H * 2 == _maxH && c.V == _maxV)
        {
            UpsampleH2V1(c, plane, stride, output);
            return output;
        }

        if (c.H * 2 == _maxH && c.V * 2 == _maxV)
        {
            UpsampleH2V2(c, plane, stride, output);
            return output;
        }

        float scaleX = (float)c.H / _maxH, scaleY = (float)c.V / _maxV;
        int lastX = c.SampleWidth - 1, lastY = c.SampleHeight - 1;

        // the source column of every output column, and its weight
        var x0 = new int[_outWidth];
        var x1 = new int[_outWidth];
        var wx = new float[_outWidth];

        for (int x = 0; x < _outWidth; x++)
        {
            float sx = (x + 0.5f) * scaleX - 0.5f;
            int ix = (int)MathF.Floor(sx);
            float f = sx - ix;

            x0[x] = Math.Clamp(ix, 0, lastX);
            x1[x] = Math.Clamp(ix + 1, 0, lastX);
            wx[x] = f;
        }

        for (int y = 0; y < _outHeight; y++)
        {
            float sy = (y + 0.5f) * scaleY - 0.5f;
            int iy = (int)MathF.Floor(sy);
            float fy = sy - iy;

            int row0 = Math.Clamp(iy, 0, lastY) * stride;
            int row1 = Math.Clamp(iy + 1, 0, lastY) * stride;

            for (int x = 0; x < _outWidth; x++)
            {
                float f = wx[x];

                float top = plane[row0 + x0[x]] * (1 - f) + plane[row0 + x1[x]] * f;
                float bottom = plane[row1 + x0[x]] * (1 - f) + plane[row1 + x1[x]] * f;

                output[y * _outWidth + x] = Clamp(top * (1 - fy) + bottom * fy);
            }
        }

        return output;
    }

    /// <summary>Twice across: libjpeg's h2v1 fancy upsampling, its rounding included.</summary>
    private void UpsampleH2V1(Component c, byte[] plane, int stride, byte[] output)
    {
        int n = c.SampleWidth;
        var wide = new byte[n * 2 + 2];

        for (int y = 0; y < _outHeight; y++)
        {
            ReadOnlySpan<byte> input = plane.AsSpan(Math.Min(y, c.SampleHeight - 1) * stride, n);

            if (n == 1)
            {
                wide[0] = wide[1] = input[0];
            }
            else
            {
                wide[0] = input[0];
                wide[1] = (byte)((input[0] * 3 + input[1] + 2) >> 2);

                for (int i = 1; i < n - 1; i++)
                {
                    int v = input[i] * 3;
                    wide[i * 2] = (byte)((v + input[i - 1] + 1) >> 2);
                    wide[i * 2 + 1] = (byte)((v + input[i + 1] + 2) >> 2);
                }

                wide[(n - 1) * 2] = (byte)((input[n - 1] * 3 + input[n - 2] + 1) >> 2);
                wide[(n - 1) * 2 + 1] = input[n - 1];
            }

            wide.AsSpan(0, _outWidth).CopyTo(output.AsSpan(y * _outWidth, _outWidth));
        }
    }

    /// <summary>Twice across and down: libjpeg's h2v2 fancy upsampling — each output row
    /// three parts its own input row and one the nearer neighbour, then the same across.</summary>
    private void UpsampleH2V2(Component c, byte[] plane, int stride, byte[] output)
    {
        int n = c.SampleWidth;
        int lastRow = c.SampleHeight - 1;
        var sums = new int[n];
        var wide = new byte[n * 2 + 2];

        for (int y = 0; y < _outHeight; y++)
        {
            int inputRow = Math.Min(y / 2, lastRow);

            // the upper output row of a pair leans on the row above, the lower on the one below
            int neighbour = Math.Clamp((y & 1) == 0 ? inputRow - 1 : inputRow + 1, 0, lastRow);

            ReadOnlySpan<byte> near = plane.AsSpan(inputRow * stride, n);
            ReadOnlySpan<byte> far = plane.AsSpan(neighbour * stride, n);

            for (int i = 0; i < n; i++) sums[i] = near[i] * 3 + far[i];

            if (n == 1)
            {
                wide[0] = wide[1] = (byte)((sums[0] * 4 + 8) >> 4);
            }
            else
            {
                wide[0] = (byte)((sums[0] * 4 + 8) >> 4);
                wide[1] = (byte)((sums[0] * 3 + sums[1] + 7) >> 4);

                for (int i = 1; i < n - 1; i++)
                {
                    int v = sums[i] * 3;
                    wide[i * 2] = (byte)((v + sums[i - 1] + 8) >> 4);
                    wide[i * 2 + 1] = (byte)((v + sums[i + 1] + 7) >> 4);
                }

                wide[(n - 1) * 2] = (byte)((sums[n - 1] * 3 + sums[n - 2] + 8) >> 4);
                wide[(n - 1) * 2 + 1] = (byte)((sums[n - 1] * 4 + 7) >> 4);
            }

            wide.AsSpan(0, _outWidth).CopyTo(output.AsSpan(y * _outWidth, _outWidth));
        }
    }

    #endregion
}