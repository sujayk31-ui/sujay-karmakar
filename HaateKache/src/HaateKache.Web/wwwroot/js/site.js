(function () {
    var toggle = document.querySelector(".nav-toggle");
    var nav = document.getElementById("site-nav");
    if (toggle && nav) {
        toggle.addEventListener("click", function () {
            var open = nav.classList.toggle("is-open");
            toggle.setAttribute("aria-expanded", open ? "true" : "false");
        });
    }

    var geo = document.getElementById("use-my-location");
    var geoForm = document.getElementById("geo-form");
    if (geo && geoForm && navigator.geolocation) {
        geo.addEventListener("click", function () {
            geo.disabled = true;
            navigator.geolocation.getCurrentPosition(function (position) {
                document.getElementById("geo-lat").value = position.coords.latitude;
                document.getElementById("geo-lng").value = position.coords.longitude;
                geoForm.submit();
            }, function () {
                geo.disabled = false;
                geo.textContent = "Location unavailable";
            });
        });
    }

    document.querySelectorAll(".area-preset").forEach(function (button) {
        button.addEventListener("click", function () {
            var area = document.getElementById("Area");
            var lat = document.getElementById("Latitude");
            var lng = document.getElementById("Longitude");
            if (area) area.value = button.getAttribute("data-area");
            if (lat) lat.value = button.getAttribute("data-lat");
            if (lng) lng.value = button.getAttribute("data-lng");
        });
    });

    var payRadios = document.querySelectorAll('input[name="PaymentMethod"]');
    var installments = document.getElementById("installment-fields");
    function syncPay() {
        if (!installments) return;
        var selected = document.querySelector('input[name="PaymentMethod"]:checked');
        installments.hidden = !selected || selected.value !== "Bnpl";
    }
    payRadios.forEach(function (radio) { radio.addEventListener("change", syncPay); });
    syncPay();
})();
