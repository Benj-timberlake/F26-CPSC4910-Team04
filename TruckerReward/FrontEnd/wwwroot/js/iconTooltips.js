(() => {
    const addTooltip = element => {
        if (!(element instanceof Element) || !element.matches("button, a") || !element.querySelector("svg")) return;
        if (element.hasAttribute("title")) return;

        const svgTitle = element.querySelector("svg title")?.textContent?.trim();
        const description = element.getAttribute("aria-label")?.trim()
            || element.getAttribute("aria-description")?.trim()
            || svgTitle
            || element.textContent?.trim();
        if (description) element.setAttribute("title", description);
    };

    const scan = node => {
        addTooltip(node);
        node.querySelectorAll?.("button, a").forEach(addTooltip);
    };

    scan(document);
    new MutationObserver(changes => {
        for (const change of changes) {
            change.addedNodes.forEach(node => {
                if (node.nodeType === Node.ELEMENT_NODE) scan(node);
            });
            if (change.type === "attributes") addTooltip(change.target);
        }
    }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["aria-label", "aria-description"] });
})();
