/* Service Worker لعقارك أونلاين: يخزّن ملفات الواجهة (CSS/JS/أيقونات) للعمل
   دون اتصال، ويعيد صفحة البداية المخزّنة لو انقطع النت أثناء التنقل.
   صفحات البيانات تُجلب من الشبكة أولًا دائمًا حتى لا تظهر بيانات قديمة. */
const CACHE = "aqarak-v1";
const SHELL = ["/", "/css/site.css", "/js/site.js", "/icons/icon-192.png", "/icons/icon-512.png"];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    const url = new URL(event.request.url);
    if (event.request.method !== "GET" || url.origin !== self.location.origin) {
        return;
    }
    // ملفات الواجهة: من الكاش أولًا، ثم الشبكة مع تحديث الكاش.
    if (url.pathname.startsWith("/css/") || url.pathname.startsWith("/js/") || url.pathname.startsWith("/icons/")) {
        event.respondWith(
            caches.match(event.request).then(
                (cached) =>
                    cached ||
                    fetch(event.request).then((res) => {
                        const copy = res.clone();
                        caches.open(CACHE).then((cache) => cache.put(event.request, copy));
                        return res;
                    })
            )
        );
        return;
    }
    // التنقل والصفحات: من الشبكة أولًا، ولو انقطع النت نرجع صفحة البداية المخزّنة.
    if (event.request.mode === "navigate") {
        event.respondWith(
            fetch(event.request).catch(() => caches.match("/"))
        );
    }
});
