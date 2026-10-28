package com.yd359team.zeppelinforms.rider.preview

import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory

/** View › Tool Windows › ZeppelinForms Preview: the previewer, as in Visual Studio and VS Code. */
internal class PreviewToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val panel = PreviewPanel(project)
        val content = ContentFactory.getInstance().createContent(panel, "", false)

        content.setDisposer(panel)
        toolWindow.contentManager.addContent(content)
    }
}
