package com.yd359team.zeppelinforms.rider.designer

import java.io.EOFException
import java.io.IOException
import java.net.StandardProtocolFamily
import java.net.UnixDomainSocketAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.channels.ServerSocketChannel
import java.nio.channels.SocketChannel
import java.nio.file.Files
import java.nio.file.Path
import java.security.SecureRandom
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutionException
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import kotlin.concurrent.thread

/**
 * One previewer host process and the connection to it. The plugin listens on a Unix domain
 * socket of its own, starts `dotnet ZeppelinForms.Designer.dll --socket <path>`, and the
 * host connects back. Messages from the host arrive on a reader thread.
 *
 * A Unix socket on every system — Windows 10 has had them since 1803, and the JVM since 16:
 * .NET's named pipes are sockets on Linux and macOS too, but in a folder and under a name
 * .NET picks, and the JVM has no named pipes on Windows.
 *
 * Plain JDK, nothing of the IDE: the tests run it against a real host.
 */
internal class DesignerConnection private constructor(
    private val process: Process,
    private val channel: SocketChannel,
    private val log: (String) -> Unit,
) : AutoCloseable {
    private val writeLock = Any()

    @Volatile
    private var disposed = false

    @Volatile
    private var closed = false

    /** A message from the host; called on the reader thread. */
    @Volatile
    var onMessage: (DesignerMessage) -> Unit = {}

    /** The host exited or the connection broke; called once, on the reader thread. */
    @Volatile
    var onClosed: () -> Unit = {}

    val isAlive: Boolean
        get() = !disposed && !closed && process.isAlive && channel.isOpen

    val isDisposed: Boolean
        get() = disposed

    val pid: Long
        get() = process.pid()

    /** Start the reader thread. Separate from [start], so that the handlers are set first. */
    fun startReading() {
        thread(isDaemon = true, name = "ZeppelinForms previewer connection") {
            try {
                while (true) {
                    val message = receive() ?: break
                    onMessage(message)
                }
            } catch (e: IOException) {
                if (!disposed) log("The connection to the previewer broke: ${e.message}")
            }

            closed = true
            onClosed()
        }
    }

    /** Send a message; a dead host is not an error here — [onClosed] reports it once. */
    fun send(message: DesignerMessage) {
        if (!isAlive) return

        val buffer = ByteBuffer.wrap(DesignerProtocol.encode(message))

        try {
            synchronized(writeLock) {
                while (buffer.hasRemaining()) channel.write(buffer)
            }
        } catch (_: IOException) {
            // the reader sees the same and reports it
        }
    }

    /** The next message; null when the host closed the connection. */
    private fun receive(): DesignerMessage? {
        val header = ByteBuffer.allocate(5).order(ByteOrder.LITTLE_ENDIAN)
        if (!readFully(header, atStart = true)) return null

        header.flip()
        val length = header.getInt()
        val kind = header.get().toInt() and 0xff

        if (length < 1 || length > DesignerProtocol.MAX_MESSAGE_LENGTH)
            throw IOException("A message of $length bytes: the stream is corrupted.")

        val payload = ByteBuffer.allocate(length - 1)
        readFully(payload, atStart = false)

        return DesignerProtocol.decode(kind, payload.array())
    }

    /** False at the end of the stream before the first byte; at the end anywhere else, an error. */
    private fun readFully(buffer: ByteBuffer, atStart: Boolean): Boolean {
        while (buffer.hasRemaining()) {
            if (channel.read(buffer) < 0) {
                if (atStart && buffer.position() == 0) return false
                throw EOFException("The stream ended in the middle of a message.")
            }
        }

        return true
    }

    /** Ask the host to exit; kill it if it doesn't in a moment. Doesn't block. */
    override fun close() {
        if (disposed) return

        send(ShutdownMessage)
        disposed = true

        thread(isDaemon = true, name = "ZeppelinForms previewer shutdown") {
            try {
                if (!process.waitFor(1500, TimeUnit.MILLISECONDS)) process.destroyForcibly()
            } catch (_: InterruptedException) {
                process.destroyForcibly()
            }

            try {
                channel.close()
            } catch (_: IOException) {
            }
        }
    }

    companion object {
        private const val CONNECT_TIMEOUT_MS = 20_000L

        /**
         * Start a host and wait for it to connect. Blocks: call it off the UI thread.
         *
         * @throws IOException the host didn't start or didn't connect; the message says why.
         */
        fun start(dotnet: String, host: Path, log: (String) -> Unit): DesignerConnection {
            if (!Files.isRegularFile(host)) throw IOException("The previewer's host is missing: $host")

            // short: a socket's path has room for about a hundred bytes, and macOS's
            // temporary folder takes half of that already
            val id = ByteArray(6).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
            val socketFile = Path.of(System.getProperty("java.io.tmpdir"), "zfp-$id.sock")

            val server = ServerSocketChannel.open(StandardProtocolFamily.UNIX)

            try {
                server.bind(UnixDomainSocketAddress.of(socketFile))
            } catch (e: IOException) {
                server.close()
                throw IOException("The previewer's socket could not be created at $socketFile: ${e.message}", e)
            }

            val process = try {
                ProcessBuilder(dotnet, host.toString(), "--socket", socketFile.toString())
                    .directory(host.parent.toFile())
                    .redirectErrorStream(true)
                    .start()
            } catch (e: IOException) {
                server.close()
                Files.deleteIfExists(socketFile)
                throw IOException(
                    "The previewer needs the .NET 10 runtime, and '$dotnet' did not start: ${e.message}. " +
                        "Install .NET 10, or set ZEPPELINFORMS_DOTNET to its dotnet.", e)
            }

            // its output: read, or a full pipe would stop it
            thread(isDaemon = true, name = "ZeppelinForms previewer output") {
                try {
                    process.inputStream.bufferedReader().forEachLine { if (it.isNotEmpty()) log("host: $it") }
                } catch (_: IOException) {
                }
            }

            val accepted = CompletableFuture<SocketChannel>()

            thread(isDaemon = true, name = "ZeppelinForms previewer accept") {
                try {
                    accepted.complete(server.accept())
                } catch (e: IOException) {
                    accepted.completeExceptionally(e)
                }
            }

            val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(CONNECT_TIMEOUT_MS)

            try {
                while (true) {
                    try {
                        val channel = accepted.get(100, TimeUnit.MILLISECONDS)

                        log("Previewer host started, process ${process.pid()}.")
                        return DesignerConnection(process, channel, log)
                    } catch (_: TimeoutException) {
                        if (!process.isAlive)
                            throw IOException("The previewer's host exited with code ${process.exitValue()} before connecting; see the IDE log.")

                        if (System.nanoTime() > deadline)
                            throw IOException("The previewer's host did not connect in time.")
                    } catch (e: ExecutionException) {
                        throw IOException("The previewer's host did not connect: ${e.cause?.message}", e.cause)
                    }
                }
            } catch (e: IOException) {
                process.destroyForcibly()
                throw e
            } finally {
                // one host, one connection: nobody else may come in on this path, and the
                // file is not needed any more — the JVM, unlike Node, leaves it behind
                server.close()
                Files.deleteIfExists(socketFile)
            }
        }
    }
}
