(() => {
    const key = "trucker-reward-theme";
    const apply = dark => {
        document.documentElement.dataset.theme = dark ? "dark" : "light";
    };
    let saved;
    try { saved = localStorage.getItem(key); } catch { /* Storage can be disabled. */ }
    apply(saved === "dark");

    window.truckerTheme = {
        getDarkMode: () => document.documentElement.dataset.theme === "dark",
        setDarkMode: dark => {
            apply(dark);
            try { localStorage.setItem(key, dark ? "dark" : "light"); } catch { /* Keep the current session usable. */ }
        }
    };
    window.addEventListener("storage", event => {
        if (event.key === key || event.key === null) apply(event.newValue === "dark");
    });
})();
