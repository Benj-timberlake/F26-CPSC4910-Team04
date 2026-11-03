
const activeChatbots = new WeakMap();

export function enableChatbotDrag(chatbot) {
    if (!chatbot || activeChatbots.has(chatbot)) return;

    let dragging = false;
    let moved = false;
    let startX = 0;
    let startY = 0;
    let initialLeft = 0;
    let initialTop = 0;

    function resetPosition() {
        chatbot.style.removeProperty("left");
        chatbot.style.removeProperty("top");
        chatbot.style.removeProperty("right");
        chatbot.style.removeProperty("bottom");
    }

    function pointerDown(e) {
        if (e.button !== 0 || dragging) return;

        const handle = e.target.closest(".chat-drag-handle");
        if (!handle || !chatbot.contains(handle)) return;
        if (e.target.closest("[data-no-drag]")) return;

        dragging = true;
        moved = false;

        const rect = chatbot.getBoundingClientRect();

        startX = e.clientX;
        startY = e.clientY;
        initialLeft = rect.left;
        initialTop = rect.top;

        chatbot.setPointerCapture(e.pointerId);
    }

    function pointerMove(e) {
        if (!dragging) return;

        const dx = e.clientX - startX;
        const dy = e.clientY - startY;

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
    }

    function pointerUp(e) {
        if (!dragging) return;
        dragging = false;

        if (chatbot.hasPointerCapture(e.pointerId)) {
            chatbot.releasePointerCapture(e.pointerId);
        }

        if (moved) {
            const stopClick = event => {
                event.preventDefault();
                event.stopImmediatePropagation();
            };

            chatbot.addEventListener("click", stopClick, {
                capture: true,
                once: true
            });

            setTimeout(() => {
                chatbot.removeEventListener(
                    "click", stopClick, true
                );
            }, 150);
        }
    }

    chatbot.addEventListener("pointerdown", pointerDown);
    chatbot.addEventListener("pointermove", pointerMove);
    chatbot.addEventListener("pointerup", pointerUp);
    chatbot.addEventListener("pointercancel", pointerUp);

    const cleanup = () => {
        chatbot.removeEventListener("pointerdown", pointerDown);
        chatbot.removeEventListener("pointermove", pointerMove);
        chatbot.removeEventListener("pointerup", pointerUp);
        chatbot.removeEventListener("pointercancel", pointerUp);
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
