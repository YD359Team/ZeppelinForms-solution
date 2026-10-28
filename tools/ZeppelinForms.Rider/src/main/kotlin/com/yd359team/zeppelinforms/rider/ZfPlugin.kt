package com.yd359team.zeppelinforms.rider

import com.intellij.execution.configurations.PathEnvironmentVariableUtil
import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.extensions.PluginId
import com.intellij.openapi.util.SystemInfo
import com.intellij.openapi.vfs.VirtualFile
import java.nio.file.Files
import java.nio.file.Path

/** Where the plugin's .NET programs are, and the dotnet that runs them. */
internal object ZfPlugin {
    const val ID = "com.yd359team.zeppelinforms"

    /** The plugin's folder: server/, designer/ and textmate/ are in it. */
    val home: Path
        get() = PluginManagerCore.getPlugin(PluginId.getId(ID))?.pluginPath
            ?: error("The ZeppelinForms plugin is not loaded.")

    /** The language server — the one Visual Studio and VS Code run. */
    val languageServer: Path
        get() = home.resolve("server").resolve("ZeppelinForms.LanguageServer.dll")

    /** The previewer's host. */
    val designerHost: Path
        get() = home.resolve("designer").resolve("ZeppelinForms.Designer.dll")

    /** A .zfml markup or .zss style sheet: what the language server answers about. */
    fun isZfFile(file: VirtualFile): Boolean {
        val extension = file.extension ?: return false
        return extension.equals("zfml", ignoreCase = true) || extension.equals("zss", ignoreCase = true)
    }

    /**
     * The dotnet to run the programs with: ZEPPELINFORMS_DOTNET, DOTNET_ROOT's, the PATH's,
     * or where the installers put it. The PATH alone is not enough: an IDE started from the
     * Dock or a desktop file doesn't always get the shell's.
     */
    fun dotnet(): String {
        System.getenv("ZEPPELINFORMS_DOTNET")?.takeIf { it.isNotBlank() }?.let { return it }

        val executable = if (SystemInfo.isWindows) "dotnet.exe" else "dotnet"

        System.getenv("DOTNET_ROOT")?.takeIf { it.isNotBlank() }?.let { root ->
            val candidate = Path.of(root, executable)
            if (Files.isRegularFile(candidate)) return candidate.toString()
        }

        PathEnvironmentVariableUtil.findInPath(executable)?.let { return it.path }

        val home = System.getProperty("user.home")
        val candidates = when {
            SystemInfo.isWindows -> listOf(
                Path.of(System.getenv("ProgramFiles") ?: "C:\\Program Files", "dotnet", executable),
                Path.of(home, ".dotnet", executable),
            )
            SystemInfo.isMac -> listOf(
                Path.of("/usr/local/share/dotnet/dotnet"),
                Path.of("/opt/homebrew/bin/dotnet"),
                Path.of(home, ".dotnet", "dotnet"),
            )
            else -> listOf(
                Path.of("/usr/share/dotnet/dotnet"),
                Path.of("/usr/lib/dotnet/dotnet"),
                Path.of("/usr/lib64/dotnet/dotnet"),
                Path.of("/snap/bin/dotnet"),
                Path.of(home, ".dotnet", "dotnet"),
            )
        }

        return candidates.firstOrNull { Files.isRegularFile(it) }?.toString() ?: executable
    }
}
