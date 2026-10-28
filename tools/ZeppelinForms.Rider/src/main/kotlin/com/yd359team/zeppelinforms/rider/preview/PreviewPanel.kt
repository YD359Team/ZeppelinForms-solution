package com.yd359team.zeppelinforms.rider.preview

import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.ComboBox
import com.intellij.ui.JBColor
import com.intellij.ui.components.JBCheckBox
import com.intellij.ui.components.JBLabel
import com.intellij.ui.components.JBScrollPane
import com.intellij.ui.components.JBTextArea
import com.intellij.util.ui.JBUI
import com.yd359team.zeppelinforms.rider.designer.CatalogMessage
import com.yd359team.zeppelinforms.rider.designer.DesignerMessage
import com.yd359team.zeppelinforms.rider.designer.DesignerMouseButton
import com.yd359team.zeppelinforms.rider.designer.DesignerProtocol
import com.yd359team.zeppelinforms.rider.designer.DesignerService
import com.yd359team.zeppelinforms.rider.designer.FrameMessage
import com.yd359team.zeppelinforms.rider.designer.HelloMessage
import com.yd359team.zeppelinforms.rider.designer.KeyMessage
import com.yd359team.zeppelinforms.rider.designer.OpenMessage
import com.yd359team.zeppelinforms.rider.designer.PointerAction
import com.yd359team.zeppelinforms.rider.designer.PointerMessage
import com.yd359team.zeppelinforms.rider.designer.PreviewErrorMessage
import com.yd359team.zeppelinforms.rider.designer.PreviewInfo
import com.yd359team.zeppelinforms.rider.designer.PreviewSettings
import com.yd359team.zeppelinforms.rider.designer.ProjectOutput
import com.yd359team.zeppelinforms.rider.designer.SettingsMessage
import com.yd359team.zeppelinforms.rider.designer.TextMessage
import java.awt.BorderLayout
import java.awt.CardLayout
import java.awt.Dimension
import java.awt.FlowLayout
import java.awt.Font
import java.awt.Graphics
import java.awt.Graphics2D
import java.awt.GridBagLayout
import java.awt.event.ComponentAdapter
import java.awt.event.ComponentEvent
import java.awt.event.HierarchyEvent
import java.awt.event.KeyAdapter
import java.awt.event.KeyEvent
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import java.awt.event.MouseWheelEvent
import java.awt.image.BufferedImage
import java.awt.image.DataBufferInt
import java.nio.file.Path
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JPanel
import javax.swing.Timer
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * The preview tool window's content: the list of the project's previews, the settings to
 * show them with, and the live picture, which takes the mouse and the keyboard. The same
 * window as Visual Studio's and VS Code's.
 *
 * Frames arrive on the connection's thread, often. They are turned into images there, only
 * the latest is kept, and the UI thread is asked to show it once; frames that came in
 * between are dropped — what a screen does with frames it had no time for.
 *
 * Size and zoom are two things. The size is what the preview is laid out at — its own, a
 * device's, or the window's. The zoom only changes how big the picture is: the host draws
 * the same layout with more or fewer pixels (the settings' scale is the screen's scale times
 * the zoom), so a smaller picture stays sharp and is never stretched. "Fit" picks the zoom
 * that shows the whole preview, never above 100%.
 */
internal class PreviewPanel(private val project: Project) : JPanel(BorderLayout()), Disposable, DesignerService.Listener {
    private data class Size(val width: Double, val height: Double)

    private data class Choice(val label: String, val value: String) {
        override fun toString() = label
    }

    /** A frame turned into an image, on the connection's thread. */
    private class Picture(val image: BufferedImage, val width: Int, val height: Int, val scale: Float)

    private val designer = project.service<DesignerService>()

    private val list = ComboBox<Choice>()
    private val sizeBox = ComboBox(SIZES.map { it.first }.toTypedArray())
    private val zoomBox = ComboBox(ZOOMS.map { if (it == 0.0) "Fit" else "${(it * 100).roundToInt()}%" }.toTypedArray())
    private val themeBox = ComboBox(THEMES.map { it.ifEmpty { "Preview's theme" } }.toTypedArray())
    private val cultureBox = ComboBox(CULTURES.toTypedArray())
    private val textScaleBox = ComboBox(TEXT_SCALES.map { "${(it * 100).roundToInt()}%" }.toTypedArray())
    private val rtl = JBCheckBox("RTL")
    private val reload = JButton("Reload")
    private val zoomInfo = JBLabel()
    private val status = JBLabel()

    private val canvas = Canvas()
    private val stage = JPanel(GridBagLayout())
    private val scroll = JBScrollPane(stage)
    private val error = JBTextArea()
    private val cards = CardLayout()
    private val center = JPanel(cards)

    private val resizeTimer = Timer(150) { sendSettings() }.apply { isRepeats = false }
    private val releaseTimer = Timer(RELEASE_AFTER_MS) { releaseHost() }.apply { isRepeats = false }

    private var holdsHost = false
    private var previews: List<PreviewInfo> = emptyList()
    private var selectedId: String? = null
    private var updatingList = false

    private val pendingFrame = AtomicReference<Picture?>()
    private val frameScheduled = AtomicBoolean()

    /** The picture on screen; null — none yet. */
    private var shown: Picture? = null

    /** The zoom asked of the host with the last settings. */
    private var requestedZoom = 1.0

    /** The size the preview is laid out at, in its own pixels, from the last frame; null — no frame of this preview yet. */
    private var logicalSize: Size? = null

    /**
     * The size the last fitting zoom was computed for: a frame of nearly the same size doesn't
     * ask again, or two zooms either side of a step could ask for each other forever — a
     * frame's size is rounded to whole device pixels.
     */
    private var zoomBasis: Size? = null

    init {
        cultureBox.isEditable = true
        cultureBox.selectedItem = ""
        rtl.toolTipText = "Right-to-left layout"
        sizeBox.toolTipText = "The size the preview is laid out at"
        zoomBox.toolTipText = "Zoom: the picture only, the layout stays"
        themeBox.toolTipText = "Theme"
        cultureBox.toolTipText = "Culture"
        textScaleBox.toolTipText = "Text scale"
        reload.toolTipText = "Start the previewer anew"
        list.toolTipText = "Previews"

        val toolbar = JPanel(FlowLayout(FlowLayout.LEFT, JBUI.scale(6), JBUI.scale(4)))
        for (item in listOf<JComponent>(list, sizeBox, zoomBox, zoomInfo, themeBox, cultureBox, textScaleBox, rtl, reload))
            toolbar.add(item)

        stage.isOpaque = false
        stage.border = JBUI.Borders.empty(MARGIN)
        stage.add(canvas)

        scroll.border = JBUI.Borders.empty()

        error.isEditable = false
        error.lineWrap = true
        error.wrapStyleWord = true
        error.font = Font(Font.MONOSPACED, Font.PLAIN, error.font.size)
        error.border = JBUI.Borders.empty(12)

        center.add(scroll, PICTURE)
        center.add(JBScrollPane(error), ERROR)

        status.border = JBUI.Borders.empty(3, 6)

        add(toolbar, BorderLayout.NORTH)
        add(center, BorderLayout.CENTER)
        add(status, BorderLayout.SOUTH)

        list.addActionListener { onPreviewChosen() }
        sizeBox.addActionListener { onSizeChosen() }
        zoomBox.addActionListener { sendSettings() }
        themeBox.addActionListener { sendSettings() }
        cultureBox.addActionListener { sendSettings() }
        textScaleBox.addActionListener { sendSettings() }
        rtl.addActionListener { sendSettings() }
        reload.addActionListener { designer.refresh(true) }

        scroll.viewport.addComponentListener(object : ComponentAdapter() {
            override fun componentResized(e: ComponentEvent) {
                // the window decides the layout when it is filled, the zoom when it is fitted
                if (isFillMode || isFitZoom) resizeTimer.restart()
            }
        })

        // shown: the host is needed; hidden a while: it is let go
        addHierarchyListener { e ->
            if ((e.changeFlags and HierarchyEvent.SHOWING_CHANGED.toLong()) != 0L) {
                if (isShowing) onShown() else releaseTimer.restart()
            }
        }

        designer.addListener(this, this)
        setStatus("Open a file of a ZeppelinForms project.")
    }

    // ===== the host =====

    private fun onShown() {
        releaseTimer.stop()

        if (!holdsHost) {
            holdsHost = true
            designer.acquire()
        }
    }

    private fun releaseHost() {
        if (!holdsHost) return

        holdsHost = false
        designer.release()
    }

    override fun hostStarted() {
        setStatus(designer.assemblyPath?.let { "Loading ${it.fileName}…" } ?: "No project.")
    }

    override fun hostFailed(reason: String) = setStatus(reason)

    override fun received(message: DesignerMessage) {
        when (message) {
            is FrameMessage -> {
                // turned into an image here, off the UI thread; the latest wins
                pendingFrame.set(toPicture(message))

                if (frameScheduled.compareAndSet(false, true)) onUi { showPendingFrame() }
            }

            is CatalogMessage -> onUi { showCatalog(message) }
            is PreviewErrorMessage -> onUi { showError(message.message, message.details) }

            is HelloMessage -> if (message.protocolVersion != DesignerProtocol.VERSION) onUi {
                setStatus("The previewer speaks protocol ${message.protocolVersion}, the plugin ${DesignerProtocol.VERSION}: reinstall the plugin.")
            }

            else -> {}
        }
    }

    override fun documentActivated(file: Path) {
        // a file of the project became active: if it declares previews and the one shown
        // is not among them, show its first
        val own = previewsOf(file)
        if (own.isNotEmpty() && own.none { it.id == selectedId }) select(own[0].id)
    }

    private fun onUi(action: () -> Unit) {
        ApplicationManager.getApplication().invokeLater({ action() }, project.disposed)
    }

    // ===== the list =====

    private fun showCatalog(catalog: CatalogMessage) {
        previews = catalog.previews

        updatingList = true

        try {
            list.removeAllItems()
            for (preview in previews) list.addItem(Choice("${preview.group} › ${preview.name}", preview.id))
        } finally {
            updatingList = false
        }

        catalog.error?.let {
            showError(it, "")
            setStatus("The project could not be loaded.")
            return
        }

        setStatus(catalog.warning?.lineSequence()?.first() ?: "${previews.size} previews in ${designer.assemblyPath?.fileName}.")

        if (previews.isEmpty()) {
            // the warning says why when it knows: types that could not be loaded
            showError(
                "No previews in this project.",
                catalog.warning
                    ?: "Mark a static method returning a UIElement with [Preview], or add a form with a constructor without parameters.")
            return
        }

        // what was shown before the rebuild, or what the active file declares
        val id = selectedId?.takeIf { selected -> previews.any { it.id == selected } }
            ?: designer.activeDocumentPath?.let { previewsOf(it).firstOrNull()?.id }
            ?: previews[0].id

        select(id)
    }

    private fun select(id: String) {
        for (index in 0 until list.itemCount) {
            if (list.getItemAt(index).value != id) continue

            if (list.selectedIndex == index) onPreviewChosen() else list.selectedIndex = index
            return
        }
    }

    private fun onPreviewChosen() {
        if (updatingList) return
        val id = (list.selectedItem as? Choice)?.value ?: return

        // another preview, another size: the zoom that fitted the last one is a guess until
        // the first frame says the new size
        if (id != selectedId) logicalSize = null

        selectedId = id
        cards.show(center, PICTURE)

        designer.send(OpenMessage(id, currentSettings()))
    }

    /** The previews whose types the file declares, by name. */
    private fun previewsOf(file: Path): List<PreviewInfo> {
        val declared = ProjectOutput.declaredTypes(file)
        return previews.filter { ProjectOutput.shortName(it.typeName) in declared }
    }

    // ===== settings =====

    private val isFillMode: Boolean
        get() = SIZES[max(0, sizeBox.selectedIndex)].second < 0

    private val isFitZoom: Boolean
        get() = !isFillMode && zoomBox.selectedIndex <= 0

    /** Device pixels per pixel of the window: the screen's scale. */
    private val deviceScale: Double
        get() = graphicsConfiguration?.defaultTransform?.scaleX ?: 1.0

    /** The room the picture has, in the window's pixels. */
    private val room: Size
        get() {
            val extent = scroll.viewport.extentSize
            val margin = 2.0 * JBUI.scale(MARGIN)
            return Size(max(1.0, extent.width - margin), max(1.0, extent.height - margin))
        }

    private fun onSizeChosen() {
        // a filled window is shown as it is: zooming it would only leave empty room
        zoomBox.isEnabled = !isFillMode

        // the fitting zoom of the previous size says nothing about this one
        logicalSize = null
        sendSettings()
    }

    private fun currentSettings(): PreviewSettings {
        val (_, width, height) = SIZES[max(0, sizeBox.selectedIndex)]
        val culture = (cultureBox.editor.item ?: cultureBox.selectedItem)?.toString()?.trim().orEmpty()

        val size = if (width < 0) {
            // the visible area, less the margin around the picture; in the window's pixels,
            // which are the preview's own
            room
        } else {
            // 0 — the preview's own: the host keeps the size the form or the attribute asks for;
            // a device's size is known before the first frame
            if (width > 0) logicalSize = Size(width, height)
            Size(width, height)
        }

        zoomBasis = logicalSize
        requestedZoom = zoomFor(logicalSize)
        zoomInfo.text = if (isFitZoom) "${(requestedZoom * 100).roundToInt()}%" else ""

        return PreviewSettings(
            width = size.width.toFloat(),
            height = size.height.toFloat(),
            // device pixels per pixel of the preview: the host draws the zoom, not Swing
            scale = (deviceScale * requestedZoom).toFloat(),
            theme = THEMES[max(0, themeBox.selectedIndex)].ifEmpty { null },
            culture = culture.ifEmpty { null },
            rightToLeft = rtl.isSelected,
            textScale = if (textScaleBox.selectedIndex > 0) TEXT_SCALES[textScaleBox.selectedIndex] else 0f,
        )
    }

    /** The zoom for a preview of this size: the chosen one, or the one that shows it whole — never larger than life. */
    private fun zoomFor(logical: Size?): Double {
        if (isFillMode) return 1.0
        if (!isFitZoom) return ZOOMS[zoomBox.selectedIndex]
        if (logical == null || logical.width <= 0 || logical.height <= 0) return requestedZoom

        // a window not laid out yet has no room to speak of: the first picture would be a stamp
        val extent = scroll.viewport.extentSize
        if (extent.width <= 2 * JBUI.scale(MARGIN) || extent.height <= 2 * JBUI.scale(MARGIN)) return requestedZoom

        val fit = min(1.0, min(room.width / logical.width, room.height / logical.height))

        // an odd zoom draws fractional pixels at every edge; a step of 1/64 is invisible
        return max(0.1, floor(fit * 64) / 64)
    }

    private fun sendSettings() {
        if (selectedId == null) return
        designer.send(SettingsMessage(currentSettings()))
    }

    // ===== the picture =====

    private fun showPendingFrame() {
        frameScheduled.set(false)
        val picture = pendingFrame.getAndSet(null) ?: return

        // the preview's own size, whatever zoom it came with
        val logical = Size(picture.width / picture.scale.toDouble(), picture.height / picture.scale.toDouble())
        logicalSize = logical

        // the first frame of a preview tells its size: if the fitting zoom is another, this
        // frame is not shown — the next one, of the right size, follows at once
        if (isFitZoom && !nearlySame(zoomBasis, logical) && abs(zoomFor(logical) - requestedZoom) > 0.001) {
            sendSettings()
            return
        }

        shown = picture
        cards.show(center, PICTURE)
        canvas.revalidate()
        canvas.repaint()
    }

    private fun nearlySame(a: Size?, b: Size?) =
        a != null && b != null && abs(a.width - b.width) < 2 && abs(a.height - b.height) < 2

    private fun showError(message: String, details: String) {
        error.text = if (details.isEmpty()) message else "$message\n\n$details"
        error.caretPosition = 0
        cards.show(center, ERROR)
    }

    private fun setStatus(text: String) {
        status.text = text
    }

    /**
     * The picture, one image pixel to one device pixel: drawn under the inverse of the screen's
     * scale, so that Swing doesn't scale it again.
     */
    private inner class Canvas : JComponent() {
        init {
            isFocusable = true

            // Tab and the arrows belong to the preview while it has the focus
            focusTraversalKeysEnabled = false

            val mouse = object : MouseAdapter() {
                override fun mouseMoved(e: MouseEvent) = pointer(PointerAction.MOVE, e)

                override fun mouseDragged(e: MouseEvent) = pointer(PointerAction.MOVE, e)

                override fun mouseExited(e: MouseEvent) = pointer(PointerAction.LEAVE, e)

                override fun mousePressed(e: MouseEvent) {
                    requestFocusInWindow()
                    pointer(PointerAction.DOWN, e)
                }

                override fun mouseReleased(e: MouseEvent) = pointer(PointerAction.UP, e)

                override fun mouseWheelMoved(e: MouseWheelEvent) {
                    // 120 per notch, positive up; a touchpad's fractions add up
                    val delta = -(e.preciseWheelRotation * 120).roundToInt()
                    if (delta != 0) pointer(PointerAction.WHEEL, e, delta)
                }
            }

            addMouseListener(mouse)
            addMouseMotionListener(mouse)
            addMouseWheelListener(mouse)

            addKeyListener(object : KeyAdapter() {
                override fun keyPressed(e: KeyEvent) = key(e, isDown = true)

                override fun keyReleased(e: KeyEvent) = key(e, isDown = false)

                override fun keyTyped(e: KeyEvent) {
                    val c = e.keyChar
                    if (c == KeyEvent.CHAR_UNDEFINED || Character.isISOControl(c)) return

                    // a shortcut is not text; AltGr comes as Ctrl+Alt on Windows
                    if ((e.isControlDown && !e.isAltDown) || e.isMetaDown) return

                    if (selectedId != null) designer.send(TextMessage(c.toString()))
                    e.consume()
                }
            })
        }

        override fun getPreferredSize(): Dimension {
            val picture = shown ?: return Dimension(0, 0)
            val scale = deviceScale
            return Dimension((picture.width / scale).roundToInt(), (picture.height / scale).roundToInt())
        }

        override fun getMinimumSize(): Dimension = preferredSize

        override fun paintComponent(g: Graphics) {
            val picture = shown ?: return
            val g2 = g.create() as Graphics2D

            try {
                val sx = g2.transform.scaleX
                val sy = g2.transform.scaleY

                g2.scale(1 / sx, 1 / sy)
                g2.drawImage(picture.image, 0, 0, null)

                g2.color = JBColor.border()
                g2.drawRect(0, 0, picture.width - 1, picture.height - 1)
            } finally {
                g2.dispose()
            }
        }

        private fun pointer(action: Int, e: MouseEvent, wheelDelta: Int = 0) {
            val picture = shown ?: return
            if (selectedId == null) return

            // the window's pixels → device pixels → the preview's own
            val toPreview = deviceScale / picture.scale

            designer.send(
                PointerMessage(
                    action = action,
                    x = (e.x * toPreview).toFloat(),
                    y = (e.y * toPreview).toFloat(),
                    button = when (e.button) {
                        MouseEvent.BUTTON2 -> DesignerMouseButton.MIDDLE
                        MouseEvent.BUTTON3 -> DesignerMouseButton.RIGHT
                        else -> DesignerMouseButton.LEFT
                    },
                    wheelDelta = wheelDelta,
                    modifiers = KeyCodes.modifiers(e),
                ))
        }

        private fun key(e: KeyEvent, isDown: Boolean) {
            if (selectedId == null) return

            val code = KeyCodes.windowsCode(e)
            if (code == 0) return

            designer.send(KeyMessage(isDown, "", code, KeyCodes.modifiers(e), isRepeat = false))
            e.consume()
        }
    }

    override fun dispose() {
        resizeTimer.stop()
        releaseTimer.stop()
        releaseHost()
    }

    private companion object {
        const val PICTURE = "picture"
        const val ERROR = "error"

        /** The empty space around the picture. */
        const val MARGIN = 24

        /** Hidden this long, the window lets the host go; shown again, it starts one. */
        const val RELEASE_AFTER_MS = 2 * 60 * 1000

        val THEMES = listOf("", "Light", "Dark", "FluentLight", "FluentDark", "HighContrastBlack", "HighContrastWhite")
        val CULTURES = listOf("", "en-US", "ru-RU", "de-DE", "ja-JP", "ar-SA", "he-IL")

        /** Width −1: the window's size; 0: the preview's own. */
        val SIZES = listOf(
            Triple("The preview's own size", 0.0, 0.0),
            Triple("Fill the window", -1.0, -1.0),
            Triple("Phone 360 × 640", 360.0, 640.0),
            Triple("Tablet 768 × 1024", 768.0, 1024.0),
            Triple("Desktop 1280 × 800", 1280.0, 800.0),
        )

        /** 0 — fit the window. */
        val ZOOMS = listOf(0.0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0)
        val TEXT_SCALES = listOf(1f, 1.25f, 1.5f, 2f)

        /** The host's RGBA, premultiplied, as an image of the same premultiplied pixels: no conversion of alpha. */
        fun toPicture(frame: FrameMessage): Picture {
            val image = BufferedImage(max(1, frame.width), max(1, frame.height), BufferedImage.TYPE_INT_ARGB_PRE)
            val target = (image.raster.dataBuffer as DataBufferInt).data
            val source = frame.pixels
            val count = min(target.size, source.size / 4)

            for (i in 0 until count) {
                val o = i * 4
                val r = source[o].toInt() and 0xff
                val g = source[o + 1].toInt() and 0xff
                val b = source[o + 2].toInt() and 0xff
                val a = source[o + 3].toInt() and 0xff
                target[i] = (a shl 24) or (r shl 16) or (g shl 8) or b
            }

            return Picture(image, frame.width, frame.height, frame.scale)
        }
    }
}
