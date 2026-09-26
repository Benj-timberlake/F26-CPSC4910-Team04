document.addEventListener("mousemove", function (event) {
    const x = (event.clientX / window.innerWidth) * 100;
    const y = (event.clientY / window.innerHeight) * 100;

    document.documentElement.style.setProperty("--mouse-x", x + "%");
    document.documentElement.style.setProperty("--mouse-y", y + "%");
});
