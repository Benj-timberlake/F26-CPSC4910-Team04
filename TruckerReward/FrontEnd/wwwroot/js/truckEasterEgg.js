
let inactivityTimer;
let truck = null;
let active = false;
let cleanupListeners = null;

const INACTIVITY_DELAY = 10000; // 10 seconds

function removeTruck() {
    if (truck) {
        truck.remove();
        truck = null;
    }
}

function showTruck() {
    if (!active || truck) return;

    truck = document.createElement("div");
    truck.className = "secret-truck";
    truck.textContent = "🚛";
    truck.title = "Click me!";

    truck.addEventListener("click", (event) => {
        event.stopPropagation();

        alert("🚛 HONK HONK! You've discovered the secret truck!");

        removeTruck();
        resetTimer();
    });

    truck.addEventListener("animationend", () => {
        removeTruck();
    });

    document.body.appendChild(truck);
}

function resetTimer() {
    if (!active) return;

    clearTimeout(inactivityTimer);

    // Don't spawn another truck while one is driving.
    if (truck) return;

    inactivityTimer = setTimeout(showTruck, INACTIVITY_DELAY);
}

export function initializeTruckEasterEgg() {
    if (active) return;

    active = true;

    const events = ["mousemove", "mousedown", "keydown", "scroll", "touchstart"];

    events.forEach(eventName => {
        window.addEventListener(eventName, resetTimer, { passive: true });
    });

    cleanupListeners = () => {
        events.forEach(eventName => {
            window.removeEventListener(eventName, resetTimer);
        });
    };

    resetTimer();
}

export function disposeTruckEasterEgg() {
    active = false;
    clearTimeout(inactivityTimer);
    cleanupListeners?.();
    cleanupListeners = null;
    removeTruck();
}
