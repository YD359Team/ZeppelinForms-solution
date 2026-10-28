package com.yd359team.zeppelinforms.rider.preview

import com.yd359team.zeppelinforms.rider.designer.DesignerModifiers
import java.awt.event.InputEvent
import java.awt.event.KeyEvent

/**
 * Java's key codes as Windows virtual-key codes — the values of ZeppelinForms' Key, which
 * the protocol speaks. Letters, digits, the numeric keypad, F1–F12, the arrows and most of
 * the editing keys have the same numbers in both; the rest are mapped here. A key without a
 * Windows code — a letter of a non-Latin layout, which Java reports as an extended code —
 * gives 0 and is not sent: its text still is.
 */
internal object KeyCodes {
    private val mapped = mapOf(
        KeyEvent.VK_ENTER to 0x0D,
        KeyEvent.VK_DELETE to 0x2E,
        KeyEvent.VK_INSERT to 0x2D,
        KeyEvent.VK_HELP to 0x2F,
        KeyEvent.VK_PRINTSCREEN to 0x2C,
        KeyEvent.VK_META to 0x5B,
        KeyEvent.VK_WINDOWS to 0x5B,
        KeyEvent.VK_CONTEXT_MENU to 0x5D,
        KeyEvent.VK_SEMICOLON to 0xBA,
        KeyEvent.VK_EQUALS to 0xBB,
        KeyEvent.VK_COMMA to 0xBC,
        KeyEvent.VK_MINUS to 0xBD,
        KeyEvent.VK_PERIOD to 0xBE,
        KeyEvent.VK_SLASH to 0xBF,
        KeyEvent.VK_BACK_QUOTE to 0xC0,
        KeyEvent.VK_OPEN_BRACKET to 0xDB,
        KeyEvent.VK_BACK_SLASH to 0xDC,
        KeyEvent.VK_CLOSE_BRACKET to 0xDD,
        KeyEvent.VK_QUOTE to 0xDE,
        KeyEvent.VK_LESS to 0xE2,
        KeyEvent.VK_KP_UP to 0x26,
        KeyEvent.VK_KP_DOWN to 0x28,
        KeyEvent.VK_KP_LEFT to 0x25,
        KeyEvent.VK_KP_RIGHT to 0x27,
    )

    /** The same numbers in Java and Windows. */
    private val same = setOf(
        KeyEvent.VK_BACK_SPACE, KeyEvent.VK_TAB, KeyEvent.VK_CLEAR, KeyEvent.VK_SHIFT, KeyEvent.VK_CONTROL,
        KeyEvent.VK_ALT, KeyEvent.VK_PAUSE, KeyEvent.VK_CAPS_LOCK, KeyEvent.VK_ESCAPE, KeyEvent.VK_SPACE,
        KeyEvent.VK_PAGE_UP, KeyEvent.VK_PAGE_DOWN, KeyEvent.VK_END, KeyEvent.VK_HOME,
        KeyEvent.VK_LEFT, KeyEvent.VK_UP, KeyEvent.VK_RIGHT, KeyEvent.VK_DOWN,
        KeyEvent.VK_MULTIPLY, KeyEvent.VK_ADD, KeyEvent.VK_SEPARATOR, KeyEvent.VK_SUBTRACT,
        KeyEvent.VK_DECIMAL, KeyEvent.VK_DIVIDE, KeyEvent.VK_NUM_LOCK, KeyEvent.VK_SCROLL_LOCK,
    )

    /** The Windows virtual-key code of the event's key; 0 — none. */
    fun windowsCode(e: KeyEvent): Int {
        val code = e.keyCode

        return when (code) {
            in mapped -> mapped.getValue(code)
            in same -> code
            in KeyEvent.VK_0..KeyEvent.VK_9 -> code
            in KeyEvent.VK_A..KeyEvent.VK_Z -> code
            in KeyEvent.VK_NUMPAD0..KeyEvent.VK_NUMPAD9 -> code
            in KeyEvent.VK_F1..KeyEvent.VK_F12 -> code
            // F13–F24 are a block of their own in Java
            in KeyEvent.VK_F13..KeyEvent.VK_F24 -> 0x7C + (code - KeyEvent.VK_F13)
            else -> 0
        }
    }

    /**
     * The protocol's modifier bits. Command on a Mac is Control: Cmd+C copies in the preview's
     * text boxes. The extended bits, not isMetaDown: AWT reports the right mouse button as Meta
     * in the old ones.
     */
    fun modifiers(e: InputEvent): Int {
        val ex = e.modifiersEx

        return (if ((ex and InputEvent.SHIFT_DOWN_MASK) != 0) DesignerModifiers.SHIFT else 0) or
            (if ((ex and (InputEvent.CTRL_DOWN_MASK or InputEvent.META_DOWN_MASK)) != 0) DesignerModifiers.CONTROL else 0) or
            (if ((ex and InputEvent.ALT_DOWN_MASK) != 0) DesignerModifiers.ALT else 0)
    }
}
