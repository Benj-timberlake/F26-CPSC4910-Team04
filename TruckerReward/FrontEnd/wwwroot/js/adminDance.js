
export function showAdminDance() {
    // Only show after a successful admin login
    const shouldDance = document.cookie
        .split("; ")
        .some(cookie => cookie === "adminDance=1");

    if (!shouldDance) return;

    // Remove the login flag so it only happens once
    document.cookie =
        "adminDance=; Path=/dashboard; Max-Age=0; SameSite=Lax";

    const images = [
        "/dancing/dance1.png",
        "/dancing/dance2.png",
        "/dancing/dance3.png",
        "/dancing/dance4.png"
    ];

    // Choose a different image from the previous one
    const last = Number(sessionStorage.getItem("lastDance"));
    const previous = Number.isInteger(last) &&
        last >= 0 && last < images.length ? last : -1;

    const choices = images.map((_, i) => i)
        .filter(i => i !== previous);

    const index = choices[
        Math.floor(Math.random() * choices.length)
    ];

    sessionStorage.setItem("lastDance", String(index));

    // Create the image
    const img = document.createElement("img");
    img.src = images[index];
    img.alt = "Dancing professor";
    img.style.cssText = `
        position: fixed;
        width: min(350px, 80vw);
        height: auto;
        max-height: 80vh;
        object-fit: contain;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        z-index: 99999;
        pointer-events: none;
        border-radius: 16px;
        box-shadow: 0 10px 40px rgba(0,0,0,0.3);
    `;

    document.body.appendChild(img);

    // Remove after 0.5 seconds
    setTimeout(() => {
        img.remove();
    }, 1000);
}
