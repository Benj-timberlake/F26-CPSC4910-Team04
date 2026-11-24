(() => {
    const key = "trucker-reward-theme";
    const listeners = new Map();
    let nextListenerId = 0;
    const apply = dark => {
        document.documentElement.dataset.theme = dark ? "dark" : "light";
        for (const listener of listeners.values()) {
            listener.invokeMethodAsync("OnThemeChanged", dark).catch(() => {});
        }
    };
    let saved;
    try { saved = localStorage.getItem(key); } catch { /* Storage can be disabled. */ }
    apply(saved === "dark");

    window.truckerTheme = {
        getDarkMode: () => document.documentElement.dataset.theme === "dark",
        setDarkMode: dark => {
            apply(dark);
            try { localStorage.setItem(key, dark ? "dark" : "light"); } catch { /* Keep the current session usable. */ }
        },
        subscribe: dotnetReference => {
            const id = String(++nextListenerId);
            listeners.set(id, dotnetReference);
            return id;
        },
        unsubscribe: id => listeners.delete(id)
    };
    window.addEventListener("storage", event => {
        if (event.key === key || event.key === null) apply(event.newValue === "dark");
    });
})();
