using System.Globalization;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Geometry;

/// <summary>
/// The path data of SVG — <c>M10 10 h5 a2 2 0 0 1 2 2 z</c> — into a <see cref="VectorPath"/>:
/// every command of SVG 1.1, absolute and relative, with the shorthand curves and the
/// compact number syntax icon sets use (<c>1.5.5</c>, <c>1-2</c>, packed arc flags).
/// </summary>
/// <remarks>
/// Parsed paths are kept by their text: an icon is drawn every frame from the same string,
/// and the parse costs more than the flattening. A path in the cache is never changed
/// again, so frames on several threads read it safely. Data broken partway keeps what came
/// before the error, as browsers draw it.
/// </remarks>
internal static class SvgPathParser
{
    private const int CacheLimit = 1024;

    private static readonly Dictionary<string, VectorPath> Cache = new(StringComparer.Ordinal);
    private static readonly Lock CacheLock = new();

    public static VectorPath Get(string data)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(data, out VectorPath? cached)) return cached;
        }

        VectorPath path = Parse(data);

        lock (CacheLock)
        {
            // an application of a thousand icons is unusual; one generating paths is
            // not, and must not grow the cache without end
            if (Cache.Count >= CacheLimit) Cache.Clear();

            Cache[data] = path;
        }

        return path;
    }

    public static VectorPath Parse(string data)
    {
        var path = new VectorPath();
        var reader = new Reader(data);

        char command = '\0';
        Point lastControl = default;
        char lastCommand = '\0';

        while (true)
        {
            reader.SkipSeparators();
            if (reader.AtEnd) break;

            char c = reader.Peek;

            if (IsCommand(c))
            {
                command = c;
                reader.Advance();
            }
            else if (command == '\0' || !reader.StartsNumber)
            {
                // a number before any command, or garbage: stop where the data breaks
                break;
            }
            else if (command is 'M')
            {
                // coordinates after a moveto are linetos
                command = 'L';
            }
            else if (command is 'm')
            {
                command = 'l';
            }

            bool relative = char.IsLower(command);
            Point current = path.Current;

            try
            {
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        {
                            Point p = reader.Point();
                            path.MoveTo(relative ? Offset(current, p) : p);
                            break;
                        }

                    case 'L':
                        {
                            Point p = reader.Point();
                            path.LineTo(relative ? Offset(current, p) : p);
                            break;
                        }

                    case 'H':
                        {
                            float x = reader.Number();
                            path.LineTo(new Point(relative ? current.X + x : x, current.Y));
                            break;
                        }

                    case 'V':
                        {
                            float y = reader.Number();
                            path.LineTo(new Point(current.X, relative ? current.Y + y : y));
                            break;
                        }

                    case 'C':
                        {
                            Point c1 = reader.Point(), c2 = reader.Point(), p = reader.Point();

                            if (relative)
                            {
                                c1 = Offset(current, c1);
                                c2 = Offset(current, c2);
                                p = Offset(current, p);
                            }

                            path.CubicTo(c1, c2, p);
                            lastControl = c2;
                            break;
                        }

                    case 'S':
                        {
                            Point c2 = reader.Point(), p = reader.Point();

                            if (relative)
                            {
                                c2 = Offset(current, c2);
                                p = Offset(current, p);
                            }

                            // the first control mirrors the last curve's second, after a C or S
                            Point c1 = char.ToUpperInvariant(lastCommand) is 'C' or 'S'
                                ? Reflect(lastControl, current)
                                : current;

                            path.CubicTo(c1, c2, p);
                            lastControl = c2;
                            break;
                        }

                    case 'Q':
                        {
                            Point c1 = reader.Point(), p = reader.Point();

                            if (relative)
                            {
                                c1 = Offset(current, c1);
                                p = Offset(current, p);
                            }

                            path.QuadTo(c1, p);
                            lastControl = c1;
                            break;
                        }

                    case 'T':
                        {
                            Point p = reader.Point();
                            if (relative) p = Offset(current, p);

                            Point c1 = char.ToUpperInvariant(lastCommand) is 'Q' or 'T'
                                ? Reflect(lastControl, current)
                                : current;

                            path.QuadTo(c1, p);
                            lastControl = c1;
                            break;
                        }

                    case 'A':
                        {
                            float rx = reader.Number();
                            float ry = reader.Number();
                            float rotation = reader.Number();
                            bool largeArc = reader.Flag();
                            bool sweep = reader.Flag();
                            Point p = reader.Point();

                            if (relative) p = Offset(current, p);

                            path.SvgArcTo(rx, ry, rotation, largeArc, sweep, p);
                            break;
                        }

                    case 'Z':
                        path.Close();
                        break;

                    default:
                        return path;
                }
            }
            catch (FormatException)
            {
                return path;
            }

            lastCommand = command;

            // a close takes no numbers: a number after it is an error, not a repeat
            if (command is 'Z' or 'z') command = '\0';
        }

        return path;
    }

    private static bool IsCommand(char c) => "MmLlHhVvCcSsQqTtAaZz".Contains(c, StringComparison.Ordinal);

    private static Point Offset(Point from, Point by) => new(from.X + by.X, from.Y + by.Y);

    private static Point Reflect(Point control, Point around) =>
        new(2 * around.X - control.X, 2 * around.Y - control.Y);

    private ref struct Reader(string text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _position;

        public readonly bool AtEnd => _position >= _text.Length;

        public readonly char Peek => _text[_position];

        public void Advance() => _position++;

        public readonly bool StartsNumber =>
            !AtEnd && (char.IsAsciiDigit(Peek) || Peek is '-' or '+' or '.');

        public void SkipSeparators()
        {
            while (!AtEnd && (char.IsWhiteSpace(Peek) || Peek == ','))
                _position++;
        }

        public Point Point()
        {
            float x = Number();
            float y = Number();
            return new Point(x, y);
        }

        /// <summary>An arc's flag: a single 0 or 1, which may run into the next number.</summary>
        public bool Flag()
        {
            SkipSeparators();

            if (AtEnd || Peek is not ('0' or '1')) throw new FormatException("An arc flag was expected.");

            bool value = Peek == '1';
            _position++;
            return value;
        }

        /// <summary>A number in SVG's syntax: a sign, digits, one point, an exponent. A second
        /// point or a sign ends it and begins the next number.</summary>
        public float Number()
        {
            SkipSeparators();

            int start = _position;

            if (!AtEnd && Peek is '-' or '+') _position++;

            bool digits = false;
            bool point = false;

            while (!AtEnd)
            {
                char c = Peek;

                if (char.IsAsciiDigit(c))
                {
                    digits = true;
                    _position++;
                }
                else if (c == '.' && !point)
                {
                    point = true;
                    _position++;
                }
                else
                {
                    break;
                }
            }

            if (!digits) throw new FormatException("A number was expected.");

            // an exponent only if digits follow it: "e" alone is not part of the number
            if (!AtEnd && Peek is 'e' or 'E')
            {
                int mark = _position;
                _position++;

                if (!AtEnd && Peek is '-' or '+') _position++;

                if (!AtEnd && char.IsAsciiDigit(Peek))
                {
                    while (!AtEnd && char.IsAsciiDigit(Peek)) _position++;
                }
                else
                {
                    _position = mark;
                }
            }

            return float.Parse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}