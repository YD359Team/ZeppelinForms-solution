package com.yd359team.zeppelinforms.rider.designer

import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets

/**
 * The previewer's wire protocol — tools/ZeppelinForms.Design/Protocol/DesignerProtocol.cs
 * spoken from Kotlin. A message is `[int32 length][byte kind][payload]`, little-endian, the
 * length counting the kind and the payload. Strings are those of .NET's BinaryWriter: a
 * 7-bit encoded byte count, then UTF-8. A nullable string is a bool, then the string when
 * the bool is true.
 *
 * Kept in step with the C# file by hand: when a message there changes shape, its Version
 * rises, and so does [VERSION] here. The tests read the bytes .NET wrote.
 */
internal object DesignerProtocol {
    const val VERSION = 1

    /** Anything longer is a corrupted stream, not a message. */
    const val MAX_MESSAGE_LENGTH = 256 * 1024 * 1024

    /** A whole message, framed. */
    fun encode(message: DesignerMessage): ByteArray {
        val payload = Writer().also { message.write(it) }.toByteArray()
        val framed = ByteBuffer.allocate(5 + payload.size).order(ByteOrder.LITTLE_ENDIAN)

        framed.putInt(payload.size + 1)
        framed.put(message.kind.toByte())
        framed.put(payload)

        return framed.array()
    }

    /** A message from its kind and payload. */
    fun decode(kind: Int, payload: ByteArray): DesignerMessage {
        val r = Reader(payload)

        return when (kind) {
            HelloMessage.KIND -> HelloMessage(r.int32(), r.string())
            LoadMessage.KIND -> LoadMessage(r.string())
            CatalogMessage.KIND -> {
                val previews = List(r.int32()) {
                    PreviewInfo(r.string(), r.string(), r.string(), r.string(), r.nullable(), r.bool(), r.settings())
                }
                CatalogMessage(previews, r.nullable(), r.nullable())
            }
            OpenMessage.KIND -> OpenMessage(r.string(), r.settings())
            SettingsMessage.KIND -> SettingsMessage(r.settings())
            FrameMessage.KIND -> {
                val width = r.int32()
                val height = r.int32()
                val scale = r.float()
                FrameMessage(width, height, scale, r.bytes(r.int32()))
            }
            PreviewErrorMessage.KIND -> PreviewErrorMessage(r.string(), r.string())
            PointerMessage.KIND -> PointerMessage(r.byte(), r.float(), r.float(), r.byte(), r.int32(), r.int32())
            KeyMessage.KIND -> KeyMessage(r.bool(), r.string(), r.int32(), r.int32(), r.bool())
            TextMessage.KIND -> TextMessage(r.string())
            CheckSheetMessage.KIND -> CheckSheetMessage(r.string())
            SheetDiagnosticsMessage.KIND -> {
                val path = r.string()
                val items = List(r.int32()) { SheetDiagnostic(r.bool(), r.string(), r.string(), r.int32(), r.int32()) }
                SheetDiagnosticsMessage(path, items)
            }
            LogMessage.KIND -> LogMessage(r.string())
            ShutdownMessage.KIND -> ShutdownMessage
            else -> throw IOException("Unknown message kind $kind.")
        }
    }

    internal class Writer {
        private val out = ByteArrayOutputStream(256)
        private val scratch = ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN)

        fun byte(value: Int) = out.write(value and 0xff)

        fun bool(value: Boolean) = byte(if (value) 1 else 0)

        fun int32(value: Int) {
            scratch.clear()
            scratch.putInt(value)
            out.write(scratch.array(), 0, 4)
        }

        fun float(value: Float) {
            scratch.clear()
            scratch.putFloat(value)
            out.write(scratch.array(), 0, 4)
        }

        fun string(value: String) {
            val bytes = value.toByteArray(StandardCharsets.UTF_8)
            var count = bytes.size

            // seven bits a byte, the high bit set on all but the last
            while (count >= 0x80) {
                byte((count and 0x7f) or 0x80)
                count = count ushr 7
            }

            byte(count)
            out.write(bytes)
        }

        fun nullable(value: String?) {
            bool(value != null)
            if (value != null) string(value)
        }

        fun bytes(value: ByteArray) = out.write(value)

        fun settings(value: PreviewSettings) {
            float(value.width)
            float(value.height)
            float(value.scale)
            nullable(value.theme)
            nullable(value.culture)
            bool(value.rightToLeft)
            float(value.textScale)
        }

        fun toByteArray(): ByteArray = out.toByteArray()
    }

    internal class Reader(payload: ByteArray) {
        private val buffer = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)

        private fun need(count: Int) {
            if (buffer.remaining() < count) throw EOFException("The message ended before its payload did.")
        }

        fun byte(): Int {
            need(1)
            return buffer.get().toInt() and 0xff
        }

        fun bool(): Boolean = byte() != 0

        fun int32(): Int {
            need(4)
            return buffer.getInt()
        }

        fun float(): Float {
            need(4)
            return buffer.getFloat()
        }

        fun string(): String {
            var count = 0
            var shift = 0

            while (true) {
                val b = byte()
                count = count or ((b and 0x7f) shl shift)
                if ((b and 0x80) == 0) break

                shift += 7
                if (shift > 28) throw IOException("A string length of more than five bytes.")
            }

            return String(bytes(count), StandardCharsets.UTF_8)
        }

        fun nullable(): String? = if (bool()) string() else null

        fun bytes(count: Int): ByteArray {
            need(count)
            return ByteArray(count).also { buffer.get(it) }
        }

        fun settings() = PreviewSettings(
            width = float(),
            height = float(),
            scale = float(),
            theme = nullable(),
            culture = nullable(),
            rightToLeft = bool(),
            textScale = float(),
        )
    }
}

/**
 * How a preview is shown. Zero sizes and null names mean "the preview's own choice, or the
 * host's default".
 */
internal data class PreviewSettings(
    /** The size in device-independent pixels. */
    val width: Float = 0f,
    val height: Float = 0f,
    /** Device pixels per device-independent pixel: the screen's scale times the zoom. */
    val scale: Float = 1f,
    val theme: String? = null,
    val culture: String? = null,
    val rightToLeft: Boolean = false,
    /** 0 — 1. */
    val textScale: Float = 0f,
)

/** One entry of the previewer's list. */
internal data class PreviewInfo(
    /** Stable across builds: the full type name, and for a method its name after a dot. */
    val id: String,
    val name: String,
    val group: String,
    /** The full name of the declaring type. */
    val typeName: String,
    /** The method's name; null for a form. */
    val memberName: String?,
    val isForm: Boolean,
    val defaults: PreviewSettings = PreviewSettings(),
)

internal data class SheetDiagnostic(val isError: Boolean, val message: String, val source: String, val line: Int, val column: Int)

/** The protocol's own modifier bits. */
internal object DesignerModifiers {
    const val SHIFT = 1
    const val CONTROL = 2
    const val ALT = 4
}

internal object PointerAction {
    const val MOVE = 0
    const val DOWN = 1
    const val UP = 2
    const val WHEEL = 3

    /** The pointer left the preview: hover ends. */
    const val LEAVE = 4
}

internal object DesignerMouseButton {
    const val LEFT = 0
    const val MIDDLE = 1
    const val RIGHT = 2
}

internal sealed class DesignerMessage(val kind: Int) {
    internal abstract fun write(w: DesignerProtocol.Writer)
}

/** The first message of each side: the protocol version, and the framework version of the host. */
internal data class HelloMessage(val protocolVersion: Int, val frameworkVersion: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.int32(protocolVersion)
        w.string(frameworkVersion)
    }

    companion object {
        const val KIND = 1
    }
}

/** IDE → host: load the project's built assembly; the host answers with [CatalogMessage]. */
internal data class LoadMessage(val assemblyPath: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) = w.string(assemblyPath)

    companion object {
        const val KIND = 2
    }
}

/** Host → IDE: the previews of the loaded assembly. */
internal data class CatalogMessage(val previews: List<PreviewInfo>, val error: String?, val warning: String?) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.int32(previews.size)

        for (p in previews) {
            w.string(p.id)
            w.string(p.name)
            w.string(p.group)
            w.string(p.typeName)
            w.nullable(p.memberName)
            w.bool(p.isForm)
            w.settings(p.defaults)
        }

        w.nullable(error)
        w.nullable(warning)
    }

    companion object {
        const val KIND = 3
    }
}

/** IDE → host: show a preview. */
internal data class OpenMessage(val previewId: String, val settings: PreviewSettings) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.string(previewId)
        w.settings(settings)
    }

    companion object {
        const val KIND = 4
    }
}

/** IDE → host: the panel was resized, the theme or another setting changed. */
internal data class SettingsMessage(val settings: PreviewSettings) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) = w.settings(settings)

    companion object {
        const val KIND = 5
    }
}

/** Host → IDE: a picture of the preview. RGBA, premultiplied, rows top to bottom. */
internal class FrameMessage(val width: Int, val height: Int, val scale: Float, val pixels: ByteArray) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.int32(width)
        w.int32(height)
        w.float(scale)
        w.int32(pixels.size)
        w.bytes(pixels)
    }

    companion object {
        const val KIND = 6
    }
}

/** Host → IDE: the preview could not be built or threw while running. */
internal data class PreviewErrorMessage(val message: String, val details: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.string(message)
        w.string(details)
    }

    companion object {
        const val KIND = 7
    }
}

/** IDE → host: the mouse over the preview, in device-independent pixels. */
internal data class PointerMessage(
    val action: Int,
    val x: Float,
    val y: Float,
    val button: Int = DesignerMouseButton.LEFT,
    /** For [PointerAction.WHEEL]: 120 per notch, positive up. */
    val wheelDelta: Int = 0,
    val modifiers: Int = 0,
) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.byte(action)
        w.float(x)
        w.float(y)
        w.byte(button)
        w.int32(wheelDelta)
        w.int32(modifiers)
    }

    companion object {
        const val KIND = 8
    }
}

/** IDE → host: a key went down or up — a Windows virtual-key code, the values of ZeppelinForms' Key. */
internal data class KeyMessage(val isDown: Boolean, val key: String, val code: Int, val modifiers: Int, val isRepeat: Boolean) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.bool(isDown)
        w.string(key)
        w.int32(code)
        w.int32(modifiers)
        w.bool(isRepeat)
    }

    companion object {
        const val KIND = 9
    }
}

/** IDE → host: typed text, after keyboard layouts and dead keys. */
internal data class TextMessage(val text: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) = w.string(text)

    companion object {
        const val KIND = 10
    }
}

/** IDE → host: read a style sheet and report its problems. */
internal data class CheckSheetMessage(val path: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) = w.string(path)

    companion object {
        const val KIND = 11
    }
}

/** Host → IDE: the problems of a sheet; an empty list clears earlier ones. */
internal data class SheetDiagnosticsMessage(val path: String, val items: List<SheetDiagnostic>) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) {
        w.string(path)
        w.int32(items.size)

        for (item in items) {
            w.bool(item.isError)
            w.string(item.message)
            w.string(item.source)
            w.int32(item.line)
            w.int32(item.column)
        }
    }

    companion object {
        const val KIND = 12
    }
}

/** Host → IDE: a line for the IDE's log. */
internal data class LogMessage(val text: String) : DesignerMessage(KIND) {
    override fun write(w: DesignerProtocol.Writer) = w.string(text)

    companion object {
        const val KIND = 13
    }
}

/** IDE → host: exit. */
internal object ShutdownMessage : DesignerMessage(14) {
    const val KIND = 14

    override fun write(w: DesignerProtocol.Writer) {}
}
