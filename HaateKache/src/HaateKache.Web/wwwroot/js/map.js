(function () {
    var mapEl = document.getElementById("store-map");
    var dataEl = document.getElementById("store-map-data");
    if (!mapEl || !dataEl) return;

    var stores = [];
    try { stores = JSON.parse(dataEl.textContent || "[]"); } catch (error) { stores = []; }
    var lat = parseFloat(mapEl.dataset.lat);
    var lng = parseFloat(mapEl.dataset.lng);

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, function (ch) {
            return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" })[ch];
        });
    }

    function popupHtml(store) {
        return "<strong>" + escapeHtml(store.name) + "</strong><br>" +
            escapeHtml(store.area) + " · " + store.distance + " km<br>" +
            escapeHtml(store.address) + "<br>" +
            "<a href=\"/stores/" + Number(store.id) + "\">Shop this store</a>";
    }

    if (mapEl.dataset.provider === "leaflet" && window.L) {
        var icon = L.icon({
            iconUrl: "/lib/leaflet/images/marker-icon.png",
            iconRetinaUrl: "/lib/leaflet/images/marker-icon-2x.png",
            shadowUrl: "/lib/leaflet/images/marker-shadow.png",
            iconSize: [25, 41],
            iconAnchor: [12, 41],
            popupAnchor: [1, -34],
            shadowSize: [41, 41]
        });
        var map = L.map(mapEl).setView([lat, lng], 12);
        L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: "&copy; OpenStreetMap"
        }).addTo(map);
        L.circleMarker([lat, lng], { radius: 8, color: "#1d6b45", fillColor: "#e0b15a", fillOpacity: 0.9 })
            .addTo(map)
            .bindPopup("You are here");
        var bounds = [[lat, lng]];
        stores.forEach(function (store) {
            bounds.push([store.lat, store.lng]);
            L.marker([store.lat, store.lng], { icon: icon }).addTo(map).bindPopup(popupHtml(store));
        });
        if (bounds.length > 1) map.fitBounds(bounds, { padding: [24, 24] });
        return;
    }

    window.initGoogleMap = function () {
        var map = new google.maps.Map(mapEl, {
            center: { lat: lat, lng: lng },
            zoom: 12,
            mapTypeControl: false
        });
        new google.maps.Marker({
            position: { lat: lat, lng: lng },
            map: map,
            title: "You are here"
        });
        var bounds = new google.maps.LatLngBounds();
        bounds.extend({ lat: lat, lng: lng });
        stores.forEach(function (store) {
            var position = { lat: store.lat, lng: store.lng };
            bounds.extend(position);
            var marker = new google.maps.Marker({ position: position, map: map, title: store.name });
            var info = new google.maps.InfoWindow({ content: popupHtml(store) });
            marker.addListener("click", function () { info.open({ map: map, anchor: marker }); });
        });
        if (stores.length > 0) map.fitBounds(bounds);
    };
})();
