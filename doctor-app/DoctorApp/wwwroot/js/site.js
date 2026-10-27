(function () {
  const companyFilter = document.getElementById("company-filter");
  companyFilter?.addEventListener("input", () => {
    const query = companyFilter.value.trim().toLowerCase();
    document.querySelectorAll("[data-company]").forEach((row) => {
      row.hidden = query.length > 0 && !row.dataset.company.includes(query);
    });
  });

  const testFilter = document.getElementById("test-filter");
  testFilter?.addEventListener("input", () => {
    const query = testFilter.value.trim().toLowerCase();
    document.querySelectorAll("[data-test]").forEach((row) => {
      row.hidden = query.length > 0 && !row.dataset.test.includes(query);
    });
    document.querySelectorAll("fieldset[data-group]").forEach((group) => {
      const visible = group.querySelector("[data-test]:not([hidden])");
      group.hidden = query.length > 0 && !visible;
    });
  });
})();
