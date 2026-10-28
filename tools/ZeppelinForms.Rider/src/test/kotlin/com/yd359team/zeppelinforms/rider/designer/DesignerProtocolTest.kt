package com.yd359team.zeppelinforms.rider.designer

import com.yd359team.zeppelinforms.rider.preview.KeyCodes
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.Test
import java.awt.event.KeyEvent
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import javax.swing.JPanel

/**
 * The protocol's codec against bytes .NET's DesignerChannel wrote — the same vectors the VS
 * Code extension's tests read —, the key codes, the project's output, and, when a host is at
 * hand, a whole conversation with it:
 *
 *     ZF_DESIGNER_HOST=…/ZeppelinForms.Designer.dll ZF_PREVIEW_ASSEMBLY=…/App.dll ./gradlew test
 */
class DesignerProtocolTest {
    /** Written by DesignerChannel.Send, in hex. */
    private val dotnet = mapOf(
        "hello" to
            "06000000010100000000",
        "open" to
            "34000000041a5a465f5368617265644c69622e53686f77636173652e44656d6f0000b443000020440000c03f01044461726b00010000a03f",
        "pointer" to
            "13000000080300002841000040c00288ffffff03000000",
        "key" to
            "0c0000000901000d0000000400000001",
        "text" to
            "dd0000000ada01d09fd180d0b8d0b2d0b5d1822c20e29c88207878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878",
        "catalog" to
            "58000000030200000003412e420142014103412e42000100000000000000000000803f0000000000000005412e432e4d014d014103412e4301014d000000c842000048420000803f01054c696768740000000000000001047761726e",
        "frame" to
            "1900000006020000000100000000000040080000000102030405060708",
        "error" to
            "0b0000000704626f6f6d0461742058",
    )

    private val messages: Map<String, DesignerMessage> = mapOf(
        "hello" to HelloMessage(1, ""),
        "open" to OpenMessage(
            "ZF_SharedLib.Showcase.Demo",
            PreviewSettings(width = 360f, height = 640f, scale = 1.5f, theme = "Dark", culture = null, rightToLeft = true, textScale = 1.25f),
        ),
        "pointer" to PointerMessage(PointerAction.WHEEL, 10.5f, -3f, DesignerMouseButton.RIGHT, -120, DesignerModifiers.CONTROL or DesignerModifiers.SHIFT),
        "key" to KeyMessage(isDown = true, key = "", code = 13, modifiers = DesignerModifiers.ALT, isRepeat = true),
        "text" to TextMessage("Привет, ✈ " + "x".repeat(200)),
        "catalog" to CatalogMessage(
            listOf(
                PreviewInfo("A.B", "B", "A", "A.B", null, true),
                PreviewInfo("A.C.M", "M", "A", "A.C", "M", false, PreviewSettings(width = 100f, height = 50f, theme = "Light")),
            ),
            null,
            "warn",
        ),
        "frame" to FrameMessage(2, 1, 2f, byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8)),
        "error" to PreviewErrorMessage("boom", "at X"),
    )

    @Test
    fun writtenAsDotnetWritesThem() {
        for ((name, message) in messages)
            assertEquals(dotnet.getValue(name), hex(DesignerProtocol.encode(message)), name)
    }

    @Test
    fun readAsDotnetWroteThem() {
        for ((name, message) in messages) {
            val bytes = unhex(dotnet.getValue(name))
            val read = DesignerProtocol.decode(bytes[4].toInt() and 0xff, bytes.copyOfRange(5, bytes.size))

            if (message is FrameMessage) {
                read as FrameMessage
                assertEquals(message.width, read.width)
                assertEquals(message.height, read.height)
                assertEquals(message.scale, read.scale)
                assertArrayEquals(message.pixels, read.pixels)
            } else {
                assertEquals(message, read, name)
            }
        }
    }

    @Test
    fun aLongStringTakesALengthOfSeveralBytes() {
        val text = "й".repeat(70000)
        val bytes = DesignerProtocol.encode(LogMessage(text))

        assertEquals(LogMessage(text), DesignerProtocol.decode(LogMessage.KIND, bytes.copyOfRange(5, bytes.size)))
    }

    @Test
    fun aShortPayloadIsAnError() {
        assertThrows(IOException::class.java) { DesignerProtocol.decode(HelloMessage.KIND, byteArrayOf(1, 0)) }
    }

    @Test
    fun javaKeysAreWindowsKeys() {
        val source = JPanel()

        fun code(key: Int) = KeyCodes.windowsCode(KeyEvent(source, KeyEvent.KEY_PRESSED, 0, 0, key, KeyEvent.CHAR_UNDEFINED))

        assertEquals(0x41, code(KeyEvent.VK_A))
        assertEquals(0x0D, code(KeyEvent.VK_ENTER))
        assertEquals(0x2E, code(KeyEvent.VK_DELETE))
        assertEquals(0xBE, code(KeyEvent.VK_PERIOD))
        assertEquals(0x25, code(KeyEvent.VK_LEFT))
        assertEquals(0x7C, code(KeyEvent.VK_F13))
        assertEquals(0, code(KeyEvent.VK_UNDEFINED))
    }

    @Test
    fun theNewestBuildOfTheProject() {
        val root = Files.createTempDirectory("zf-project")

        try {
            val project = root.resolve("App").resolve("App.csproj")
            Files.createDirectories(project.parent)
            Files.writeString(project, "<Project><PropertyGroup><AssemblyName>My.App</AssemblyName></PropertyGroup></Project>")

            val source = project.resolveSibling("Forms").resolve("MainForm.cs")
            Files.createDirectories(source.parent)
            Files.writeString(source, "public partial class MainForm : Form { }")

            assertEquals(project, ProjectOutput.projectOf(source, root))
            assertNull(ProjectOutput.outputOf(project).assembly)

            val debug = touch(project.resolveSibling("bin/Debug/net10.0/My.App.dll"), 1_000_000)
            touch(project.resolveSibling("bin/Debug/net10.0/ref/My.App.dll"), 3_000_000)
            assertEquals(debug, ProjectOutput.outputOf(project).assembly)

            val release = touch(project.resolveSibling("bin/Release/net10.0/My.App.dll"), 2_000_000)
            assertEquals(release, ProjectOutput.outputOf(project).assembly)

            assertEquals(setOf("MainForm"), ProjectOutput.declaredTypes(source))
        } finally {
            root.toFile().deleteRecursively()
        }
    }

    @Test
    fun aConversationWithTheHost() {
        val host = System.getenv("ZF_DESIGNER_HOST")
        val assembly = System.getenv("ZF_PREVIEW_ASSEMBLY")
        assumeTrue(host != null && assembly != null, "ZF_DESIGNER_HOST and ZF_PREVIEW_ASSEMBLY are not set")

        val received = LinkedBlockingQueue<DesignerMessage>()
        val connection = DesignerConnection.start(System.getenv("ZF_DOTNET") ?: "dotnet", Path.of(host!!)) { }

        connection.onMessage = { received.add(it) }
        connection.startReading()

        try {
            connection.send(HelloMessage(DesignerProtocol.VERSION, ""))
            connection.send(LoadMessage(assembly!!))

            val hello = next<HelloMessage>(received)
            assertEquals(DesignerProtocol.VERSION, hello.protocolVersion)

            val catalog = next<CatalogMessage>(received)
            assertNull(catalog.error)
            assertTrue(catalog.previews.isNotEmpty())

            connection.send(OpenMessage(catalog.previews[0].id, PreviewSettings(width = 300f, height = 200f, scale = 2f)))

            val frame = next<FrameMessage>(received)
            assertEquals(600, frame.width)
            assertEquals(400, frame.height)
            assertEquals(600 * 400 * 4, frame.pixels.size)

            connection.send(PointerMessage(PointerAction.MOVE, 20f, 20f))
            connection.send(KeyMessage(true, "", 9, 0, false))
            connection.send(TextMessage("ж"))
            assertTrue(connection.isAlive)
        } finally {
            connection.close()
        }
    }

    private inline fun <reified T : DesignerMessage> next(queue: LinkedBlockingQueue<DesignerMessage>): T {
        while (true) {
            val message = queue.poll(20, TimeUnit.SECONDS)
            assertNotNull(message, "no ${T::class.simpleName} from the host")
            if (message is T) return message
        }
    }

    private fun touch(file: Path, time: Long): Path {
        Files.createDirectories(file.parent)
        Files.write(file, byteArrayOf())
        Files.setLastModifiedTime(file, java.nio.file.attribute.FileTime.fromMillis(time))
        return file
    }

    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it) }

    private fun unhex(text: String) = ByteArray(text.length / 2) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
}
