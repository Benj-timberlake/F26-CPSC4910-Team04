
const activeChatbots = new WeakMap();

export function enableChatbotDrag(chatbot) {
    if (!chatbot || activeChatbots.has(chatbot)) return;

    let dragging = false;
    let moved = false;
    let startX = 0;
    let startY = 0;
    let initialLeft = 0;
    let initialTop = 0;
    let pointerId = null;
    let suppressClick = false;

    function pointerDown(e) {
        if (e.button !== 0 || dragging) return;

        const handle = e.target.closest(".chat-drag-handle");

        if (!handle || !chatbot.contains(handle)) return;
        if (e.target.closest("[data-no-drag]")) return;

        dragging = true;
        moved = false;
        suppressClick = false;
        pointerId = e.pointerId;

        const rect = chatbot.getBoundingClientRect();

        startX = e.clientX;
        startY = e.clientY;
        initialLeft = rect.left;
        initialTop = rect.top;
    }

    function pointerMove(e) {
        if (!dragging || e.pointerId !== pointerId) return;

        const dx = e.clientX - startX;
        const dy = e.clientY - startY;

        // Only start dragging after moving 5 pixels.
        if (!moved && Math.hypot(dx, dy) < 5) return;

        moved = true;

        const maxLeft = Math.max(
            0, window.innerWidth - chatbot.offsetWidth
        );

        const maxTop = Math.max(
            0, window.innerHeight - chatbot.offsetHeight
        );

        chatbot.style.left =
            Math.max(0, Math.min(initialLeft + dx, maxLeft)) + "px";

        chatbot.style.top =
            Math.max(0, Math.min(initialTop + dy, maxTop)) + "px";

        chatbot.style.right = "auto";
        chatbot.style.bottom = "auto";

        e.preventDefault();
    }

    function pointerUp(e) {
        if (!dragging || e.pointerId !== pointerId) return;

        dragging = false;
        pointerId = null;

        // Suppress clicks only if the user actually dragged.
        suppressClick = moved;
    }

    function preventDragClick(e) {
        if (suppressClick) {
            e.preventDefault();
            e.stopImmediatePropagation();
            suppressClick = false;
        }
    }

    function cancelDrag() {
        dragging = false;
        pointerId = null;
    }

    chatbot.addEventListener("pointerdown", pointerDown);

    document.addEventListener("pointermove", pointerMove, {
        passive: false
    });

    document.addEventListener("pointerup", pointerUp);
    document.addEventListener("pointercancel", cancelDrag);

    // Capture clicks only to prevent accidental opening after dragging.
    chatbot.addEventListener("click", preventDragClick, true);

    const cleanup = () => {
        chatbot.removeEventListener("pointerdown", pointerDown);
        document.removeEventListener("pointermove", pointerMove);
        document.removeEventListener("pointerup", pointerUp);
        document.removeEventListener("pointercancel", cancelDrag);
        chatbot.removeEventListener("click", preventDragClick, true);
    };

    activeChatbots.set(chatbot, cleanup);
}

export function resetChatbotPosition(chatbot) {
    if (!chatbot) return;

    chatbot.style.removeProperty("left");
    chatbot.style.removeProperty("top");
    chatbot.style.removeProperty("right");
    chatbot.style.removeProperty("bottom");
}

export function disableChatbotDrag(chatbot) {
    const cleanup = activeChatbots.get(chatbot);

    if (cleanup) {
        cleanup();
        activeChatbots.delete(chatbot);
    }
}
