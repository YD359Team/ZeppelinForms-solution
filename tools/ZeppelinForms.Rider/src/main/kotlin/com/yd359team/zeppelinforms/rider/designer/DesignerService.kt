package com.yd359team.zeppelinforms.rider.designer

import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerEvent
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.util.concurrency.AppExecutorUtil
import com.yd359team.zeppelinforms.rider.ZfPlugin
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

/**
 * The previewer's host for the project being edited: started when the preview needs it,
 * started anew after each build of that project and when a file of another project becomes
 * active — as in Visual Studio and VS Code.
 *
 * A new process per build rather than a reload in place: the framework's registries in the
 * host would keep every old version of the project's controls. The preview reopens what it
 * showed, so a rebuild looks like a refresh.
 *
 * Builds are noticed by the assembly's time stamp, looked at once a second while the
 * preview is open: Rider's build events are on its .NET side, and a `dotnet build` in the
 * terminal is a build too.
 *
 * Everything here runs on the UI thread but starting a host, which waits for it to connect.
 * Messages from the host are passed on as they come, on the connection's thread: listeners
 * move to the UI thread themselves — frames come often, and only they know which they can skip.
 */
@Service(Service.Level.PROJECT)
internal class DesignerService(private val project: Project) : Disposable {
    interface Listener {
        /** A host started and loads the project: what was shown should be shown again. */
        fun hostStarted() {}

        /** The host could not be started or has gone; the text says why. */
        fun hostFailed(reason: String) {}

        /** Any message of the host, on the connection's thread. */
        fun received(message: DesignerMessage) {}

        /** A file became active: the preview shows its previews. */
        fun documentActivated(file: Path) {}
    }

    private val log = logger<DesignerService>()
    private val listeners = CopyOnWriteArrayList<Listener>()

    private var connection: DesignerConnection? = null

    // read by the build watch, on a thread of its own
    @Volatile
    private var projectFile: Path? = null

    @Volatile
    private var assemblyStamp = 0L

    private var users = 0
    private var generation = 0
    private var buildWatch: ScheduledFuture<*>? = null

    /** The project's assembly the host has loaded; null — none. */
    @Volatile
    var assemblyPath: Path? = null
        private set

    init {
        project.messageBus.connect(this).subscribe(FileEditorManagerListener.FILE_EDITOR_MANAGER, object : FileEditorManagerListener {
            override fun selectionChanged(event: FileEditorManagerEvent) {
                val file = event.newFile?.let(::localPath) ?: return

                refresh(false)
                for (listener in listeners) listener.documentActivated(file)
            }
        })
    }

    /** The file of the selected editor, for choosing its previews. */
    val activeDocumentPath: Path?
        get() = FileEditorManager.getInstance(project).selectedFiles.firstNotNullOfOrNull(::localPath)

    fun addListener(listener: Listener, parent: Disposable) {
        listeners.add(listener)
        Disposer.register(parent) { listeners.remove(listener) }
    }

    /** Somebody needs the host; the first one starts it, the last [release] stops it. */
    fun acquire() {
        if (users++ == 0) {
            refresh(false)
            watchBuilds()
        }
    }

    fun release() {
        if (--users > 0) return

        users = 0
        buildWatch?.cancel(false)
        buildWatch = null
        stop()
    }

    fun send(message: DesignerMessage) {
        connection?.send(message)
    }

    /**
     * Start a host for the active project if there is none for it, or if the project was built
     * since. [force] — start a new one regardless.
     */
    fun refresh(force: Boolean) {
        if (users == 0) return

        val document = activeDocumentPath
        val root = project.basePath?.let { Path.of(it) }

        // a file of no project — a solution item, a scratch — keeps what is shown
        val file = document?.let { ProjectOutput.projectOf(it, root) } ?: projectFile

        val output = file?.let(ProjectOutput::outputOf)
        val assembly = output?.let { it.assembly ?: it.binDirectory.resolve(it.assemblyName + ".dll") }
        val stamp = ProjectOutput.stampOf(assembly)

        if (!force && connection?.isAlive == true && assembly == assemblyPath && stamp == assemblyStamp) return

        projectFile = file
        assemblyPath = assembly
        assemblyStamp = stamp

        restart(assembly)
    }

    /** Once a second while somebody needs the host: a newer build of the project's assembly restarts it. */
    private fun watchBuilds() {
        buildWatch = AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay({
            val file = projectFile ?: return@scheduleWithFixedDelay
            val output = ProjectOutput.outputOf(file)
            val newest = output.assembly ?: return@scheduleWithFixedDelay

            // refresh looks again, on the UI thread, and restarts the host
            if (newest != assemblyPath || ProjectOutput.stampOf(newest) != assemblyStamp)
                ApplicationManager.getApplication().invokeLater({ refresh(false) }, project.disposed)
        }, 1, 1, TimeUnit.SECONDS)
    }

    private fun restart(assembly: Path?) {
        stop()

        val started = ++generation

        // starting waits for the process to connect: off the UI thread
        ApplicationManager.getApplication().executeOnPooledThread(Runnable {
            val newConnection = try {
                DesignerConnection.start(ZfPlugin.dotnet(), ZfPlugin.designerHost) { log.info(it) }
            } catch (e: IOException) {
                log.warn(e.message)
                ApplicationManager.getApplication().invokeLater({
                    if (started == generation) fail(e.message ?: "The previewer did not start.")
                }, project.disposed)
                return@Runnable
            }

            ApplicationManager.getApplication().invokeLater({ attach(newConnection, started, assembly) }, project.disposed)
        })
    }

    private fun attach(newConnection: DesignerConnection, started: Int, assembly: Path?) {
        // a newer start has begun meanwhile, or nobody needs a host any more
        if (started != generation || users == 0) {
            newConnection.close()
            return
        }

        connection = newConnection

        newConnection.onMessage = { message ->
            if (message is LogMessage) log.info(message.text)
            for (listener in listeners) listener.received(message)
        }

        newConnection.onClosed = {
            ApplicationManager.getApplication().invokeLater({
                if (started == generation && !newConnection.isDisposed)
                    fail("The previewer's host has stopped; see the IDE log.")
            }, project.disposed)
        }

        newConnection.startReading()
        newConnection.send(HelloMessage(DesignerProtocol.VERSION, ""))

        val loading = assembly != null && Files.isRegularFile(assembly)

        if (loading) {
            log.info("Loading $assembly")
            newConnection.send(LoadMessage(assembly.toString()))
        }

        for (listener in listeners) listener.hostStarted()

        // after hostStarted, whose listeners report "loading…": the reason there is nothing
        // to load must be the last word
        if (!loading) {
            fail(
                if (assembly == null) "Open a file of a ZeppelinForms project to see its previews."
                else "Build the project to see its previews: ${assembly.fileName} is not there yet.")
        }
    }

    private fun fail(reason: String) {
        for (listener in listeners) listener.hostFailed(reason)
    }

    private fun stop() {
        val old = connection
        connection = null
        old?.close()
    }

    override fun dispose() {
        users = 0
        buildWatch?.cancel(false)
        buildWatch = null
        stop()
    }

    private companion object {
        fun localPath(file: VirtualFile): Path? =
            if (file.isInLocalFileSystem) file.toNioPath() else null
    }
}
