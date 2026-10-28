// The names before 2026.1.4 — LspServerSupportProvider, ProjectWideLspServerDescriptor —:
// renamed since, deprecated, and still working, they run on every Rider from 2025.2.1 on.
@file:Suppress("DEPRECATION")

package com.yd359team.zeppelinforms.rider.lsp

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import com.yd359team.zeppelinforms.rider.ZfPlugin
import java.nio.file.Files

/**
 * The ZeppelinForms language server for .zfml and .zss: completion, hover and problems —
 * the server Visual Studio and VS Code run, over stdin and stdout. One per project, started
 * with the first such file.
 */
internal class ZfLspServerSupportProvider : LspServerSupportProvider {
    override fun fileOpened(project: Project, file: VirtualFile, serverStarter: LspServerSupportProvider.LspServerStarter) {
        if (ZfPlugin.isZfFile(file)) serverStarter.ensureServerStarted(ZfLspServerDescriptor(project))
    }
}

private class ZfLspServerDescriptor(project: Project) : ProjectWideLspServerDescriptor(project, "ZeppelinForms") {
    override fun isSupportedFile(file: VirtualFile): Boolean = ZfPlugin.isZfFile(file)

    override fun createCommandLine(): GeneralCommandLine {
        val server = ZfPlugin.languageServer

        if (!Files.isRegularFile(server)) throw ExecutionException("The ZeppelinForms language server is missing from the plugin: $server")

        // the server's own folder: it finds the framework's assembly and documentation next to it
        return GeneralCommandLine(ZfPlugin.dotnet(), server.toString())
            .withWorkDirectory(server.parent.toString())
    }
}
