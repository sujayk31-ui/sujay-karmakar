(function () {
  const list = document.getElementById("medicine-list");
  const add = document.getElementById("add-medicine");
  if (!list || !add) return;

  function rows() {
    return [...list.querySelectorAll("[data-med]")];
  }

  function reindex() {
    rows().forEach((row, index) => {
      row.querySelectorAll("[name]").forEach((input) => {
        input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${index}]`);
      });
    });
  }

  function bind(row) {
    const name = row.querySelector("[data-name]");
    const box = row.querySelector(".suggest");
    let timer = 0;
    name.addEventListener("input", () => {
      row.querySelector("[data-id]").value = "";
      window.clearTimeout(timer);
      const query = name.value.trim();
      if (query.length < 2) {
        box.hidden = true;
        box.innerHTML = "";
        return;
      }
      timer = window.setTimeout(async () => {
        const response = await fetch(`/Prescriptions/New?handler=Suggest&q=${encodeURIComponent(query)}`, { credentials: "same-origin" });
        if (!response.ok) return;
        const hits = await response.json();
        box.innerHTML = "";
        hits.forEach((hit) => {
          const button = document.createElement("button");
          button.type = "button";
          const price = hit.price == null ? "No MRP in source" : `₹${Number(hit.price).toFixed(2)}`;
          button.textContent = `${hit.name} — ${hit.composition || ""} (${price})`;
          button.addEventListener("click", () => {
            name.value = hit.name;
            row.querySelector("[data-id]").value = hit.id;
            row.querySelector("[data-composition]").value = hit.composition || "";
            row.querySelector("[data-company]").value = hit.company || "";
            box.hidden = true;
          });
          box.appendChild(button);
        });
        box.hidden = hits.length === 0;
      }, 250);
    });
    row.querySelector(".remove-med")?.addEventListener("click", () => {
      if (rows().length === 1) {
        row.querySelectorAll("input").forEach((input) => { input.value = ""; });
        return;
      }
      row.remove();
      reindex();
    });
  }

  rows().forEach(bind);
  add.addEventListener("click", () => {
    const next = rows()[0].cloneNode(true);
    next.querySelectorAll("input").forEach((input) => { input.value = ""; });
    next.querySelector(".suggest").innerHTML = "";
    next.querySelector(".suggest").hidden = true;
    list.appendChild(next);
    reindex();
    bind(next);
  });
})();
