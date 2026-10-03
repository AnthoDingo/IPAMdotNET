document.getElementById("theme-toggle")?.addEventListener("click", () => {
    const root = document.documentElement;
    const theme = root.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
    root.setAttribute("data-bs-theme", theme);
    try { localStorage.setItem("theme", theme); } catch { }
});
