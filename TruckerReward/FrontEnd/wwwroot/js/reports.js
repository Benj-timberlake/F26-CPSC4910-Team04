const libs = ["lib/jspdf/jspdf.umd.min.js", "lib/jspdf/jspdf.plugin.autotable.min.js"];
let loading;

function load(src) {
    return new Promise((resolve, reject) => {
        const tag = document.createElement("script");
        tag.src = src;
        tag.onload = resolve;
        tag.onerror = () => reject(new Error("couldn't load " + src));
        document.head.appendChild(tag);
    });
}

function ensureLibs() {
    // autotable needs jspdf loaded first
    loading ??= libs.reduce((p, src) => p.then(() => load(src)), Promise.resolve()).catch(e => {
        loading = null;
        throw e;
    });
    return loading;
}

export async function pdf({ title, subtitle, columns, rows, filename }) {
    await ensureLibs();
    const doc = new window.jspdf.jsPDF({ unit: "pt", format: "letter" });
    const margin = 40;
    doc.setFontSize(18);
    doc.text(title, margin, 50);
    doc.setFontSize(10);
    doc.setTextColor(100);
    const lines = doc.splitTextToSize(subtitle || "", doc.internal.pageSize.getWidth() - margin * 2);
    doc.text(lines, margin, 68);
    doc.autoTable({
        head: [columns],
        body: rows,
        startY: 68 + lines.length * 12 + 6,
        margin: { left: margin, right: margin, bottom: 40 },
        styles: { fontSize: 9, cellPadding: 4, overflow: "linebreak" },
        headStyles: { fillColor: [30, 64, 120], textColor: 255 },
        alternateRowStyles: { fillColor: [244, 246, 250] }
    });
    const pages = doc.getNumberOfPages();
    const { width, height } = doc.internal.pageSize;
    doc.setFontSize(9);
    doc.setTextColor(120);
    for (let i = 1; i <= pages; i++) {
        doc.setPage(i);
        doc.text(`Page ${i} of ${pages}`, width / 2, height - 20, { align: "center" });
    }
    doc.save(filename);
}
