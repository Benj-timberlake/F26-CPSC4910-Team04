function dismissOutside(event) {
    document.querySelectorAll('details.profile-dropdown[open]').forEach(dropdown => {
        if (!dropdown.contains(event.target)) dropdown.open = false;
    });
}

function dismissOnEscape(event) {
    if (event.key !== 'Escape') return;
    document.querySelectorAll('details.profile-dropdown[open]').forEach(dropdown => {
        dropdown.open = false;
        dropdown.querySelector('summary')?.focus();
    });
}

export function initialize() {
    document.addEventListener('pointerdown', dismissOutside);
    document.addEventListener('keydown', dismissOnEscape);
}

export function dispose() {
    document.removeEventListener('pointerdown', dismissOutside);
    document.removeEventListener('keydown', dismissOnEscape);
}
