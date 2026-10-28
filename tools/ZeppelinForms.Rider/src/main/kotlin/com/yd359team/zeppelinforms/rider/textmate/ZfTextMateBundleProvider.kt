package com.yd359team.zeppelinforms.rider.textmate

import com.yd359team.zeppelinforms.rider.ZfPlugin
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider
import java.nio.file.Files

/**
 * The grammar of .zss style sheets — Visual Studio's and VS Code's — as a TextMate bundle in
 * the plugin's textmate/zss folder: a package.json in VS Code's shape that names it. Markup
 * needs none: .zfml is XML, and the XML editor has it.
 */
internal class ZfTextMateBundleProvider : TextMateBundleProvider {
    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val bundle = ZfPlugin.home.resolve("textmate").resolve("zss")

        return if (Files.isDirectory(bundle)) listOf(TextMateBundleProvider.PluginBundle("ZeppelinForms Style Sheet", bundle)) else emptyList()
    }
}
