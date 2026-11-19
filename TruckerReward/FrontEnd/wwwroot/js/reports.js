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

const tableStyle = {
    styles: { fontSize: 9, cellPadding: 4, overflow: "linebreak" },
    headStyles: { fillColor: [30, 64, 120], textColor: 255 },
    alternateRowStyles: { fillColor: [244, 246, 250] }
};

// builds one table, or one per section
export async function pdf({ title, subtitle, columns, rows, sections, filename }) {
    await ensureLibs();
    const doc = new window.jspdf.jsPDF({ unit: "pt", format: "letter" });
    const margin = 40;
    doc.setFontSize(18);
    doc.text(title, margin, 50);
    doc.setFontSize(10);
    doc.setTextColor(100);
    const lines = doc.splitTextToSize(subtitle || "", doc.internal.pageSize.getWidth() - margin * 2);
    doc.text(lines, margin, 68);
    let y = 68 + lines.length * 12 + 6;
    const tableMargin = { left: margin, right: margin, bottom: 40 };
    if (!sections) {
        doc.autoTable({ head: [columns], body: rows, startY: y, margin: tableMargin, ...tableStyle });
    } else {
        const bottom = doc.internal.pageSize.getHeight() - 60;
        for (const { heading, columns, rows } of sections) {
            y += 14;
            if (y > bottom) {
                doc.addPage();
                y = 50;
            }
            doc.setFontSize(13);
            doc.setTextColor(30);
            doc.text(heading, margin, y);
            doc.autoTable({ head: [columns], body: rows, startY: y + 8, margin: tableMargin, ...tableStyle });
            y = doc.lastAutoTable.finalY + 10;
        }
    }
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

const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

// standalone page with the same sections as the pdf
export function html({ title, subtitle, sections, filename }) {
    const tables = sections.map(({ heading, columns, rows }) =>
        `<h2>${esc(heading)}</h2><table><thead><tr>${columns.map(c => `<th>${esc(c)}</th>`).join("")}</tr></thead>` +
        `<tbody>${rows.map(r => `<tr>${r.map(v => `<td>${esc(v)}</td>`).join("")}</tr>`).join("")}</tbody></table>`).join("");
    const page = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${esc(title)}</title><style>
body{font-family:system-ui,sans-serif;margin:32px;color:#1e1e1e}p{color:#646464}
table{border-collapse:collapse;width:100%;margin-bottom:24px;font-size:14px}
th{background:#1e4078;color:#fff;text-align:left}th,td{padding:6px 8px;border:1px solid #d5dbe5}
tbody tr:nth-child(even){background:#f4f6fa}</style></head>
<body><h1>${esc(title)}</h1><p>${esc(subtitle)}</p>${tables}</body></html>`;
    const url = URL.createObjectURL(new Blob([page], { type: "text/html" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: filename });
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
