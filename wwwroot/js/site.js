// سلوكيات واجهة عامة: قائمة الموبايل، قائمة الإشعارات، إظهار كلمة المرور.
// لا يعتمد على أي مكتبة خارجية، ويعمل مع العربية (RTL) والإنجليزية (LTR).
document.addEventListener("DOMContentLoaded", function () {
    // قائمة التنقل في الشاشات الصغيرة.
    // كانت المشكلة: القائمة بتتفتح بزرار ☰ بس مفيش طريقة واضحة
    // لقفلها غير دوسة تانية على نفس الزرار (من غير أي علامة قفل)،
    // على عكس نافذة الجرس واللوحة الجانبية اللي ليهم ✕ وخلفية وEscape.
    // فبقى للقائمة نفس مسارات القفل: ✕ على الزرار، ضغطة خارج الـ header،
    // زر Escape، أو دوسة على أي رابط جواها.
    var navToggle = document.getElementById("navToggle");
    var mainNav = document.getElementById("mainNav");
    var navBackdrop = document.getElementById("navBackdrop");
    var drawerClose = document.getElementById("drawerClose");
    function setNavOpen(open) {
        if (!mainNav) {
            return;
        }
        mainNav.classList.toggle("open", open);
        if (navToggle) {
            navToggle.setAttribute("aria-expanded", open ? "true" : "false");
            navToggle.textContent = open ? "✕" : "☰";
        }
        if (navBackdrop) { navBackdrop.hidden = !open; }
        document.body.style.overflow = open ? "hidden" : "";
    }
    if (drawerClose) {
        drawerClose.addEventListener("click", function () { setNavOpen(false); });
    }
    if (navBackdrop) {
        navBackdrop.addEventListener("click", function () { setNavOpen(false); });
    }
    if (navToggle && mainNav) {
        navToggle.addEventListener("click", function () {
            setNavOpen(!mainNav.classList.contains("open"));
        });
        mainNav.addEventListener("click", function (event) {
            if (event.target.closest("a")) {
                setNavOpen(false);
            }
        });
        document.addEventListener("click", function (event) {
            if (mainNav.classList.contains("open") &&
                !event.target.closest(".site-header")) {
                setNavOpen(false);
            }
        });
        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                setNavOpen(false);
            }
        });
    }

    // نافذة الإشعارات المنبثقة (الجرس): فتح/إغلاق بزر الجرس، أو زر ✕،
    // أو الضغط على الخلفية، أو زر Escape. بتفويض الأحداث (delegation)
    // حتى تعمل بعد استبدال HTML الجرس لحظيًا.
    function setBellOpen(open) {
        var bellDropdown = document.getElementById("bellDropdown");
        var btn = document.getElementById("bellBtn");
        if (!bellDropdown) {
            return;
        }
        if (open) {
            bellDropdown.removeAttribute("hidden");
            document.body.style.overflow = "hidden";
            setNavOpen(false);
        } else {
            bellDropdown.setAttribute("hidden", "");
            document.body.style.overflow = "";
        }
        if (btn) {
            btn.setAttribute("aria-expanded", open ? "true" : "false");
        }
    }
    document.addEventListener("click", function (event) {
        var bellDropdown = document.getElementById("bellDropdown");
        if (!bellDropdown) {
            return;
        }
        if (event.target.closest("#bellBtn")) {
            setBellOpen(bellDropdown.hasAttribute("hidden"));
            return;
        }
        if (event.target.closest("#bellClose") || event.target === bellDropdown) {
            setBellOpen(false);
        }
    });
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            setBellOpen(false);
            setAdminOpen(false);
        }
    });

    // اللوحة الجانبية للإدارة (Side Navbar): زرار واحد يفتح لوحة
    // منزلقة من الجنب بكل خصائص الأدمن. فتحها يقفل نافذة الجرس.
    // بتفويض الأحداث حتى تعمل بعد استبدال HTML الإدارة لحظيًا.
    function setAdminOpen(open) {
        var side = document.getElementById("adminSide");
        var btn = document.getElementById("adminSideBtn");
        if (!side) {
            return;
        }
        side.classList.toggle("open", open);
        document.body.style.overflow = open ? "hidden" : "";
        if (open) {
            setNavOpen(false);
        }
        if (btn) {
            btn.setAttribute("aria-expanded", open ? "true" : "false");
        }
    }
    document.addEventListener("click", function (event) {
        var side = document.getElementById("adminSide");
        if (!side) {
            return;
        }
        if (event.target.closest("#adminSideBtn")) {
            setBellOpen(false);
            setAdminOpen(!side.classList.contains("open"));
            return;
        }
        if (event.target.closest("#adminSideClose") || event.target.closest("#adminSideBackdrop")) {
            setAdminOpen(false);
        }
    });

    function refreshBell() {
        var wrap = document.querySelector(".bell-wrap");
        if (!wrap) {
            return;
        }
        fetch("/Notifications/Bell")
            .then(function (response) {
                if (!response.ok || response.url.indexOf("/Notifications/Bell") === -1) {
                    return null;
                }
                return response.text();
            })
            .then(function (html) {
                if (!html) {
                    return;
                }
                var tmp = document.createElement("div");
                tmp.innerHTML = html.trim();
                var fresh = tmp.firstElementChild;
                if (fresh) {
                    wrap.replaceWith(fresh);
                    // لو كانت النافذة مفتوحة وقت التحديث، الـ HTML الجديد
                    // يجيء مغلقًا، فلازم نرجّع تمرير الصفحة.
                    document.body.style.overflow = "";
                }
            })
            .catch(function () { /* يبقى القديم حتى التحديث التالي */ });
    }

    function refreshAdminNav() {
        var span = document.getElementById("adminNavLinks");
        var panelSpan = document.getElementById("adminSidePanel");
        if (span) {
            fetch("/Admin/NavBadges")
                .then(function (response) {
                    if (!response.ok || response.url.indexOf("/Admin/NavBadges") === -1) {
                        return null;
                    }
                    return response.text();
                })
                .then(function (html) {
                    if (html !== null) {
                        span.innerHTML = html;
                    }
                })
                .catch(function () { /* يبقى القديم حتى التحديث التالي */ });
        }
        if (panelSpan) {
            fetch("/Admin/NavBadges?panelOnly=true")
                .then(function (response) {
                    if (!response.ok || response.url.indexOf("/Admin/NavBadges") === -1) {
                        return null;
                    }
                    return response.text();
                })
                .then(function (html) {
                    if (html !== null) {
                        panelSpan.innerHTML = html;
                        // لو كانت اللوحة مفتوحة وقت التحديث، الـ HTML الجديد
                        // يجيء مغلقًا، فلازم نرجّع تمرير الصفحة.
                        document.body.style.overflow = "";
                    }
                })
                .catch(function () { /* يبقى القديم حتى التحديث التالي */ });
        }
    }

    var pollingStarted = false;
    function startPolling() {
        if (pollingStarted) {
            return;
        }
        pollingStarted = true;
        setInterval(function () {
            if (document.hidden) { return; }
            refreshBell();
            refreshAdminNav();
            checkListingsChanged();
        }, 60000);
    }

    if (window.signalR) {
        var connection = new signalR.HubConnectionBuilder()
            .withUrl("/realtimeHub")
            .withAutomaticReconnect()
            .build();
        connection.on("refreshBell", refreshBell);
        connection.on("refreshAdminNav", refreshAdminNav);
        connection.on("listingsChanged", checkListingsChanged);
        connection.start().catch(function () {
            startPolling();
        });
        connection.onclose(function () {
            startPolling();
        });
    } else {
        startPolling();
    }

    // تحديث قائمة الإعلانات لحظيًا: عند إشارة تغيير، قارن معرّفات الإعلانات
    // المعروضة حاليًا مع النسخة الحديثة من نفس الصفحة (فيحترم الفلاتر)،
    // ولو اختلفت اعرض شريطًا يضغط عليه المستخدم لتطبيق التحديث — من غير
    // إزاحة الصفحة فجأة وهو يتصفح. مقارنة المعرّفات فقط حتى لا يظهر الشريط
    // بسبب تغيّر عدّاد المشاهدات وحده.
    var pendingGridHtml = null;
    function gridListingIds(grid) {
        var ids = [];
        grid.querySelectorAll("[data-listing-id]").forEach(function (el) {
            ids.push(el.getAttribute("data-listing-id"));
        });
        return ids.join(",");
    }

    function showListingsBanner() {
        if (document.getElementById("liveListingsBanner")) {
            return;
        }
        var grid = document.querySelector(".cards-grid");
        if (!grid) {
            return;
        }
        var banner = document.createElement("div");
        banner.id = "liveListingsBanner";
        banner.className = "live-banner";
        banner.innerHTML = "يوجد تحديث في الإعلانات";
        var button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-primary";
        button.textContent = "عرض التحديث";
        button.addEventListener("click", function () {
            if (pendingGridHtml !== null) {
                grid.innerHTML = pendingGridHtml;
            }
            banner.remove();
            pendingGridHtml = null;
        });
        banner.appendChild(button);
        grid.parentNode.insertBefore(banner, grid);
    }

    function checkListingsChanged() {
        var grid = document.querySelector(".cards-grid");
        if (!grid || !grid.querySelector("[data-listing-id]")) {
            return;
        }
        fetch(window.location.href)
            .then(function (response) {
                if (!response.ok) {
                    return null;
                }
                return response.text();
            })
            .then(function (html) {
                if (!html) {
                    return;
                }
                var doc = new DOMParser().parseFromString(html, "text/html");
                var freshGrid = doc.querySelector(".cards-grid");
                if (!freshGrid) {
                    return;
                }
                if (gridListingIds(freshGrid) !== gridListingIds(grid)) {
                    pendingGridHtml = freshGrid.innerHTML;
                    showListingsBanner();
                }
            })
            .catch(function () { /* يبقى القديم حتى التحديث التالي */ });
    }

    // ضغطة على أي مكان في كارت الإعلان تفتح التفاصيل، إلا على زرار
    // أو رابط أو نموذج جواه (المفضلة، التواصل، عرض التفاصيل).
    // بتفويض الأحداث حتى تعمل بعد استبدال شبكة الكروت لحظيًا.
    document.addEventListener("click", function (event) {
        var card = event.target.closest(".listing-card");
        if (!card) {
            return;
        }
        if (event.target.closest("a, button, form, input, select, textarea, label")) {
            return;
        }
        var link = card.querySelector("h3 a");
        if (link && link.href) {
            window.location.href = link.href;
        }
    });

    // حقل الدور يظهر في نموذج الإعلان للشقة بس، وعدد الشقق للعمارة بس.
    var propertyTypeSelect = document.getElementById("PropertyType");
    var floorField = document.getElementById("floorField");
    var apartmentsCountField = document.getElementById("apartmentsCountField");
    function getPropertyTypeValue() {
        var radio = document.querySelector("input[name='PropertyType']:checked");
        if (radio) { return radio.value; }
        return propertyTypeSelect ? propertyTypeSelect.value : "";
    }
    function toggleFloorField() {
        var v = getPropertyTypeValue();
        if (floorField) {
            floorField.style.display = v === "Apartment" ? "" : "none";
        }
        if (apartmentsCountField) {
            apartmentsCountField.style.display = v === "Building" ? "" : "none";
        }
    }
    if (propertyTypeSelect && (floorField || apartmentsCountField)) {
        propertyTypeSelect.addEventListener("change", toggleFloorField);
    }
    document.querySelectorAll("input[name='PropertyType']").forEach(function (r) {
        r.addEventListener("change", toggleFloorField);
    });
    toggleFloorField();

    // عند إرسال نموذج الإعلان (رفع صور/فيديو ممكن يبطّأ)، اظهر
    // "جاري رفع الإعلان..." وعطّل زرار الحفظ لمنع الإرسال مرتين.
    var listingForm = document.getElementById("listingForm");
    if (listingForm) {
        listingForm.addEventListener("submit", function () {
            var status = document.getElementById("uploadStatus");
            if (status) {
                status.hidden = false;
            }
            var submitBtn = listingForm.querySelector("button[type=submit]");
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.textContent = "جاري رفع الإعلان...";
            }
        });
    }

    // معرض صور التفاصيل: صورة رئيسية + مصغرات + عدّاد + عرض كامل (Lightbox).
    // الأسهم والكيبورد (يمين/شمال وEscape) شغالة، والضغطة على الخلفية تقفل.
    var gallery = document.getElementById("gallery");
    if (gallery) {
        var mainImg = document.getElementById("galleryMain");
        var thumbs = Array.prototype.slice.call(gallery.querySelectorAll(".gallery-thumb"));
        var urls = thumbs.length
            ? thumbs.map(function (t) { return t.querySelector("img").src; })
            : [mainImg.src];
        var counter = document.getElementById("galleryCounter");
        var lightbox = document.getElementById("lightbox");
        var lightboxImg = document.getElementById("lightboxImg");
        var lightboxCounter = document.getElementById("lightboxCounter");
        var current = 0;
        function showPhoto(index) {
            current = (index + urls.length) % urls.length;
            mainImg.src = urls[current];
            if (counter) {
                counter.textContent = (current + 1) + " / " + urls.length;
            }
            thumbs.forEach(function (t, i) {
                t.classList.toggle("active", i === current);
            });
            if (lightboxImg) {
                lightboxImg.src = urls[current];
            }
            var lbDownload = document.getElementById("lightboxDownload");
            if (lbDownload) {
                lbDownload.href = urls[current];
            }
            if (lightboxCounter) {
                lightboxCounter.textContent = (current + 1) + " / " + urls.length;
            }
        }
        function setLightbox(open) {
            if (!lightbox) {
                return;
            }
            if (open) {
                showPhoto(current);
                lightbox.hidden = false;
                document.body.style.overflow = "hidden";
            } else {
                lightbox.hidden = true;
                document.body.style.overflow = "";
            }
        }
        if (urls.length <= 1) {
            ["galleryPrev", "galleryNext"].forEach(function (id) {
                var btn = document.getElementById(id);
                if (btn) {
                    btn.style.display = "none";
                }
            });
            if (counter) {
                counter.style.display = "none";
            }
        }
        thumbs.forEach(function (t, i) {
            t.addEventListener("click", function () { showPhoto(i); });
        });
        var gPrev = document.getElementById("galleryPrev");
        var gNext = document.getElementById("galleryNext");
        if (gPrev) {
            gPrev.addEventListener("click", function () { showPhoto(current - 1); });
        }
        if (gNext) {
            gNext.addEventListener("click", function () { showPhoto(current + 1); });
        }
        mainImg.addEventListener("click", function () { setLightbox(true); });
        var lbClose = document.getElementById("lightboxClose");
        var lbPrev = document.getElementById("lightboxPrev");
        var lbNext = document.getElementById("lightboxNext");
        if (lbClose) {
            lbClose.addEventListener("click", function () { setLightbox(false); });
        }
        if (lbPrev) {
            lbPrev.addEventListener("click", function () { showPhoto(current - 1); });
        }
        if (lbNext) {
            lbNext.addEventListener("click", function () { showPhoto(current + 1); });
        }
        if (lightbox) {
            lightbox.addEventListener("click", function (event) {
                if (event.target === lightbox || event.target === lightboxImg) {
                    setLightbox(false);
                }
            });
        }
        document.addEventListener("keydown", function (event) {
            if (!lightbox || lightbox.hidden) {
                return;
            }
            if (event.key === "Escape") {
                setLightbox(false);
            } else if (event.key === "ArrowLeft") {
                showPhoto(current - 1);
            } else if (event.key === "ArrowRight") {
                showPhoto(current + 1);
            }
        });
        // تشغيل تلقائي لمعرض التفاصيل (كل 3 ثوانٍ).
        var autoBtn = document.getElementById("galleryAutoplay");
        var autoTimer = null;
        if (autoBtn) {
            if (urls.length <= 1) {
                autoBtn.style.display = "none";
            } else {
                autoBtn.addEventListener("click", function () {
                    if (autoTimer) {
                        clearInterval(autoTimer);
                        autoTimer = null;
                        autoBtn.textContent = "▶";
                    } else {
                        autoTimer = setInterval(function () { showPhoto(current + 1); }, 3000);
                        autoBtn.textContent = "⏸";
                    }
                });
            }
        }
        // سحب (swipe) للتنقل بين صور المعرض في الموبايل.
        var galleryMain = document.querySelector(".gallery-main");
        if (galleryMain && urls.length > 1) {
            var touchX = null;
            galleryMain.addEventListener("touchstart", function (e) {
                touchX = e.changedTouches[0].clientX;
            }, { passive: true });
            galleryMain.addEventListener("touchend", function (e) {
                if (touchX === null) { return; }
                var dx = e.changedTouches[0].clientX - touchX;
                touchX = null;
                if (Math.abs(dx) > 40) { showPhoto(current + (dx < 0 ? 1 : -1)); }
            }, { passive: true });
        }
    }

    // المقارنة: اختيار حتى 3 إعلانات من الكروت أو التفاصيل، محفوظة في
    // localStorage حتى تفضل موجودة وأنت بتتنقل بين الصفحات.
    var compareKey = "aqarak-compare";
    var compareBar = document.getElementById("compareBar");
    var compareCount = document.getElementById("compareCount");
    var compareLink = document.getElementById("compareLink");
    var compareClear = document.getElementById("compareClear");
    function readCompare() {
        try {
            var raw = JSON.parse(localStorage.getItem(compareKey) || "[]");
            return Array.isArray(raw) ? raw.filter(function (n) { return Number.isInteger(n); }).slice(0, 3) : [];
        } catch (e) {
            return [];
        }
    }
    function renderCompare() {
        var ids = readCompare();
        document.querySelectorAll(".compare-toggle").forEach(function (btn) {
            var id = parseInt(btn.getAttribute("data-id"), 10);
            var selected = ids.indexOf(id) !== -1;
            btn.textContent = selected ? btn.getAttribute("data-added") : btn.getAttribute("data-add");
            btn.classList.toggle("is-selected", selected);
        });
        if (!compareBar) {
            return;
        }
        if (ids.length === 0) {
            compareBar.hidden = true;
            return;
        }
        compareCount.textContent = ids.length + " / 3";
        compareLink.href = "/Listings/Compare?ids=" + ids.join(",");
    }
    document.addEventListener("click", function (event) {
        var btn = event.target.closest(".compare-toggle");
        if (!btn) {
            return;
        }
        var id = parseInt(btn.getAttribute("data-id"), 10);
        var ids = readCompare();
        var at = ids.indexOf(id);
        if (at !== -1) {
            ids.splice(at, 1);
        } else if (ids.length >= 3) {
            var original = btn.textContent;
            btn.textContent = btn.getAttribute("data-max");
            setTimeout(function () { btn.textContent = original; }, 2000);
            return;
        } else {
            ids.push(id);
            if (compareBar) { compareBar.hidden = false; }
        }
        localStorage.setItem(compareKey, JSON.stringify(ids));
        renderCompare();
    });
    if (compareClear) {
        compareClear.addEventListener("click", function () {
            localStorage.removeItem(compareKey);
            renderCompare();
        });
    }
    renderCompare();

    // تثبيت الموقع كتطبيق (PWA): تسجيل الـ Service Worker.
    if ("serviceWorker" in navigator) {
        window.addEventListener("load", function () {
            navigator.serviceWorker.register("/sw.js").catch(function () { /* يعمل الموقع عاديًا بدونه */ });
        });
    }

    // سحب وإفلات الصور في نماذج الإعلان + معاينة قبل الرفع.
    var dropZone = document.getElementById("photoDropZone");
    if (dropZone) {
        var photoInput = dropZone.querySelector('input[type="file"]');
        var photoPreview = document.getElementById("photoPreview");
        var pickedFiles = new DataTransfer();
        function renderPhotoPreview() {
            photoPreview.innerHTML = "";
            Array.from(pickedFiles.files).forEach(function (file, index) {
                var item = document.createElement("span");
                item.className = "photo-preview-item";
                var img = document.createElement("img");
                img.src = URL.createObjectURL(file);
                img.alt = file.name;
                var remove = document.createElement("button");
                remove.type = "button";
                remove.className = "photo-preview-remove";
                remove.textContent = "✕";
                remove.setAttribute("aria-label", "إزالة " + file.name);
                remove.addEventListener("click", function () {
                    pickedFiles.items.remove(index);
                    photoInput.files = pickedFiles.files;
                    renderPhotoPreview();
                });
                var num = document.createElement("span");
                num.className = "photo-preview-num";
                num.textContent = index + 1;
                item.appendChild(img);
                item.appendChild(num);
                item.appendChild(remove);
                photoPreview.appendChild(item);
            });
        }
        function addPickedFiles(files) {
            Array.from(files).forEach(function (file) {
                if (file.type.startsWith("image/")) {
                    pickedFiles.items.add(file);
                }
            });
            photoInput.files = pickedFiles.files;
            renderPhotoPreview();
        }
        photoInput.addEventListener("change", function () {
            addPickedFiles(photoInput.files);
        });
        ["dragenter", "dragover"].forEach(function (ev) {
            dropZone.addEventListener(ev, function (e) {
                e.preventDefault();
                dropZone.classList.add("drag-over");
            });
        });
        ["dragleave", "drop"].forEach(function (ev) {
            dropZone.addEventListener(ev, function (e) {
                e.preventDefault();
                dropZone.classList.remove("drag-over");
            });
        });
        dropZone.addEventListener("drop", function (e) {
            addPickedFiles(e.dataTransfer.files);
        });
        var clearPhotosBtn = document.getElementById("clearPhotosBtn");
        if (clearPhotosBtn) {
            clearPhotosBtn.addEventListener("click", function () {
                pickedFiles = new DataTransfer();
                photoInput.files = pickedFiles.files;
                renderPhotoPreview();
            });
        }
    }

    // إعادة ترتيب الصور الحالية بالسحب في صفحة التعديل.
    var sortList = document.getElementById("existingPhotos");
    if (sortList) {
        var orderInput = document.getElementById("photoOrderInput");
        var draggedPhoto = null;
        function updatePhotoOrder() {
            var ids = Array.from(sortList.querySelectorAll("li")).map(function (li) {
                return li.getAttribute("data-id");
            });
            orderInput.value = ids.join(",");
        }
        sortList.querySelectorAll("li").forEach(function (li) {
            li.addEventListener("dragstart", function () {
                draggedPhoto = li;
                li.classList.add("dragging");
            });
            li.addEventListener("dragend", function () {
                draggedPhoto = null;
                li.classList.remove("dragging");
                updatePhotoOrder();
            });
            li.addEventListener("dragover", function (e) {
                e.preventDefault();
                if (draggedPhoto && draggedPhoto !== li) {
                    var rect = li.getBoundingClientRect();
                    var after = (e.clientX - rect.left) > rect.width / 2;
                    sortList.insertBefore(draggedPhoto, after ? li.nextSibling : li);
                }
            });
            li.addEventListener("drop", function (e) {
                e.preventDefault();
                updatePhotoOrder();
            });
        });
    }

    // نسخ رابط الإعلان: زر "نسخ الرابط" في الكارت والتفاصيل.
    // بتفويض الأحداث حتى يعمل بعد استبدال شبكة الكروت لحظيًا.
    document.addEventListener("click", function (event) {
        var btn = event.target.closest(".share-copy");
        if (!btn) {
            return;
        }
        var url = btn.getAttribute("data-url");
        if (!url) {
            return;
        }
        var done = function () {
            if (window.showToast) {
                window.showToast("تم النسخ ✓");
            }
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(url).then(done).catch(function () { fallbackCopy(url, done); });
        } else {
            fallbackCopy(url, done);
        }
    });
    function fallbackCopy(text, done) {
        var tmp = document.createElement("textarea");
        tmp.value = text;
        tmp.style.position = "fixed";
        tmp.style.opacity = "0";
        document.body.appendChild(tmp);
        tmp.select();
        try {
            document.execCommand("copy");
            done();
        } catch (e) { /* يبقى الرابط في الزر نفسه للنسخ اليدوي */ }
        document.body.removeChild(tmp);
    }

    // حاسبة القسط في صفحة التفاصيل (للبيع): تحديث لحظي أثناء الكتابة.
    var calc = document.getElementById("calculator");
    if (calc) {
        var priceInput = document.getElementById("calcPrice");
        var downInput = document.getElementById("calcDown");
        var yearsInput = document.getElementById("calcYears");
        var rateInput = document.getElementById("calcRate");
        var result = document.getElementById("calcResult");
        var fmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
        function recalc() {
            var price = parseFloat(priceInput.value) || 0;
            var down = parseFloat(downInput.value) || 0;
            var years = parseFloat(yearsInput.value) || 0;
            var rate = parseFloat(rateInput.value) || 0;
            var loan = Math.max(0, price - down);
            var months = years * 12;
            var pay = 0;
            if (months > 0 && loan > 0) {
                if (rate <= 0) {
                    pay = loan / months;
                } else {
                    var r = rate / 100 / 12;
                    pay = loan * r / (1 - Math.pow(1 + r, -months));
                }
            }
            result.textContent = fmt.format(Math.round(pay)) + " جنيه شهريًا";
            var downBar = document.getElementById("calcBarDown");
            var loanBar = document.getElementById("calcBarLoan");
            if (downBar && loanBar && price > 0) {
                var downPct = Math.min(100, Math.max(0, down / price * 100));
                downBar.style.width = downPct + "%";
                loanBar.style.width = (100 - downPct) + "%";
            }
        }
        [priceInput, downInput, yearsInput, rateInput].forEach(function (input) {
            input.addEventListener("input", recalc);
        });
        recalc();
    }

    // إظهار/إخفاء كلمة المرور أثناء الكتابة.
    document.querySelectorAll(".password-toggle").forEach(function (button) {
        button.addEventListener("click", function () {
            var field = button.closest(".password-field");
            var input = field ? field.querySelector("input") : null;
            if (!input) {
                return;
            }
            var showing = input.type === "text";
            input.type = showing ? "password" : "text";
            button.textContent = showing ? "إظهار" : "إخفاء";
            button.setAttribute("aria-label", showing ? "إظهار كلمة المرور" : "إخفاء كلمة المرور");
            input.focus();
        });
    });
});

// الوضع الفاتح/الداكن: الاختيار محفوظ في جهاز المستخدم.
document.addEventListener("DOMContentLoaded", function () {
    var themeKey = "aqarak-theme";
    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        try { localStorage.setItem(themeKey, theme); } catch (e) {}
        var toggle = document.getElementById("themeToggle");
        if (toggle) {
            toggle.textContent = theme === "light" ? "🌙" : "☀️";
        }
    }
    function systemTheme() {
        var h = new Date().getHours();
        return (h >= 6 && h < 18) ? "light" : "dark";
    }
    try {
        applyTheme(localStorage.getItem(themeKey) || systemTheme());
    } catch (e) {
        applyTheme("dark");
    }
    var themeToggle = document.getElementById("themeToggle");
    if (themeToggle) {
        themeToggle.addEventListener("click", function () {
            var current = document.documentElement.getAttribute("data-theme") === "light" ? "dark" : "light";
            applyTheme(current);
        });
    }
});

// المفضلة للزوار بدون حساب: تحفظ في جهاز الزائر (localStorage)،
// ويزامنها كوكي aqarak-guest-favs حتى يقرأها السيرفر في صفحة المفضلة.
document.addEventListener("DOMContentLoaded", function () {
    var guestFavKey = "aqarak-guest-favorites";
    function getGuestFavorites() {
        try {
            return JSON.parse(localStorage.getItem(guestFavKey) || "[]");
        } catch (e) {
            return [];
        }
    }
    function saveGuestFavorites(ids) {
        try { localStorage.setItem(guestFavKey, JSON.stringify(ids)); } catch (e) {}
        document.cookie = "aqarak-guest-favs=" + ids.join(",") + "; path=/; max-age=" + (365 * 24 * 60 * 60);
    }
    function refreshGuestFavoriteButtons() {
        var ids = getGuestFavorites();
        document.querySelectorAll(".guest-favorite").forEach(function (btn) {
            var id = parseInt(btn.getAttribute("data-id") || "0", 10);
            var on = ids.indexOf(id) !== -1;
            btn.classList.toggle("is-favorite", on);
            btn.setAttribute("title", on ? "إزالة من المفضلة" : "إضافة إلى المفضلة");
            if (btn.classList.contains("guest-fav-details")) {
                btn.textContent = on ? "♥ في المفضلة" : "♡ إضافة إلى المفضلة";
            } else {
                btn.textContent = on ? "♥" : "♡";
            }
        });
    }
    document.addEventListener("click", function (event) {
        var btn = event.target.closest ? event.target.closest(".guest-favorite") : null;
        if (!btn) {
            return;
        }
        var id = parseInt(btn.getAttribute("data-id") || "0", 10);
        if (!id) {
            return;
        }
        var ids = getGuestFavorites();
        var i = ids.indexOf(id);
        if (i === -1) {
            ids.push(id);
        } else {
            ids.splice(i, 1);
        }
        saveGuestFavorites(ids);
        refreshGuestFavoriteButtons();
    });
    document.addEventListener("click", function (event) {
        var clearBtn = event.target.closest ? event.target.closest(".clear-guest-favorites") : null;
        if (!clearBtn) {
            return;
        }
        if (!confirm("حذف كل المفضلة؟")) {
            return;
        }
        saveGuestFavorites([]);
        location.reload();
    });
    refreshGuestFavoriteButtons();
});

// معاينة الإعلان قبل النشر: تعرض شكل الكارت من قيم النموذج الحالية (بدون إرسال).
document.addEventListener("DOMContentLoaded", function () {
    var btn = document.getElementById("previewListingBtn");
    var box = document.getElementById("listingPreview");
    var form = document.getElementById("listingForm");
    if (!btn || !box || !form) {
        return;
    }
    function escapeHtml(value) {
        return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }
    function val(id) {
        var el = document.getElementById(id);
        return el ? el.value.trim() : "";
    }
    function selectedText(id) {
        var el = document.getElementById(id);
        return el && el.selectedIndex >= 0 ? el.options[el.selectedIndex].text : "";
    }
    function checked(id) {
        var el = document.getElementById(id);
        return !!(el && el.checked);
    }
    function renderPreview(scroll) {
        var facts = [];
        if (val("Rooms")) { facts.push("الغرف: " + val("Rooms")); }
        if (val("Bathrooms")) { facts.push("الحمامات: " + val("Bathrooms")); }
        if (val("Floor")) { facts.push("الدور: " + val("Floor")); }
        var furnishing = selectedText("Furnishing");
        if (furnishing && furnishing !== "غير مفروش") { facts.push(furnishing); }
        var finishing = selectedText("Finishing");
        if (finishing && finishing !== "—") { facts.push(finishing); }
        if (checked("HasParking")) { facts.push("جراج"); }
        if (checked("HasElevator")) { facts.push("أسانسير"); }
        if (checked("HasBalcony")) { facts.push("بلكونة"); }
        if (checked("HasAirConditioning")) { facts.push("تكييف"); }
        if (checked("HasSecurity")) { facts.push("أمن"); }
        if (checked("AcceptsInstallments")) { facts.push("يقبل التقسيط"); }
        // الصورة: أول صورة جديدة مختارة، وإلا أول صورة حالية في التعديل.
        var photoInput = document.getElementById("PhotoFiles");
        var photoUrl = "";
        if (photoInput && photoInput.files && photoInput.files.length > 0) {
            photoUrl = URL.createObjectURL(photoInput.files[0]);
        } else {
            var existing = document.querySelector("#existingPhotos img");
            if (existing) { photoUrl = existing.getAttribute("src") || ""; }
        }
        var html = '<article class="listing-card"><div class="availability-bar available">متاح</div>';
        html += photoUrl
            ? '<img class="listing-photo" src="' + escapeHtml(photoUrl) + '" alt="" />'
            : '<div class="listing-photo placeholder">لا توجد صورة</div>';
        html += '<div class="listing-body"><h3>' + escapeHtml(val("Title") || "عنوان الإعلان") + '</h3>';
        html += '<p class="listing-area">' + escapeHtml(selectedText("Area") || val("Area")) + '</p>';
        html += '<p class="listing-area">المساحة: ' + escapeHtml(val("AreaSqm")) + ' م²</p>';
        if (facts.length) { html += '<p class="listing-facts">' + facts.map(escapeHtml).join(" • ") + '</p>'; }
        html += '<p class="listing-price">' + escapeHtml(val("Price")) + ' جنيه</p>';
        html += '</div></article>';
        box.innerHTML = html;
        box.hidden = false;
        if (scroll) { box.scrollIntoView({ behavior: "smooth", block: "nearest" }); }
    }
    btn.addEventListener("click", function () { renderPreview(true); });
    form.addEventListener("input", function () { renderPreview(false); });
    form.addEventListener("change", function () { renderPreview(false); });
});

document.addEventListener("DOMContentLoaded", function () {
    var form = document.getElementById("listingForm");
    if (!form) { return; }
    var bar = document.getElementById("completenessBar");
    var pct = document.getElementById("completenessPct");
    var hint = document.getElementById("suggestedPriceHint");
    function val(id) { var el = document.getElementById(id); return el ? el.value.trim() : ""; }
    function checked(id) { var el = document.getElementById(id); return !!(el && el.checked); }
    function customFeaturesText() {
        var el = form.querySelector("input[name='customFeatures']");
        return el ? el.value.trim() : "";
    }
    function updateCompleteness() {
        if (!bar || !pct) { return; }
        var score = 0;
        if (val("Title")) { score += 15; }
        if (val("Description").length >= 20) { score += 15; }
        if (parseFloat(val("Price")) > 0) { score += 10; }
        if (parseFloat(val("AreaSqm")) > 0) { score += 10; }
        if (parseFloat(val("Rooms")) > 0) { score += 10; }
        var photoInput = document.getElementById("PhotoFiles");
        var hasPhoto = (photoInput && photoInput.files && photoInput.files.length > 0)
            || !!document.querySelector("#existingPhotos img");
        if (hasPhoto) { score += 20; }
        var featureChecked = form.querySelectorAll("input[name='selectedFeatures']:checked").length > 0
            || !!customFeaturesText();
        if (featureChecked) { score += 10; }
        if (val("Finishing")) { score += 10; }
        bar.style.width = score + "%";
        pct.textContent = score + "%";
    }
    var avgCache = {};
    function updateSuggestedPrice() {
        if (!hint) { return; }
        var area = val("Area");
        var areaSqm = parseFloat(val("AreaSqm")) || 0;
        var price = parseFloat(val("Price")) || 0;
        if (!area) { hint.textContent = ""; return; }
        function render(avg) {
            if (!avg || avg <= 0) {
                hint.textContent = "لا توجد إعلانات كافية في هذه المنطقة لحساب متوسط.";
                return;
            }
            var text = "متوسط سعر المتر في " + area + ": " + Math.round(avg).toLocaleString("en-US") + " ج.م";
            if (areaSqm > 0) {
                text += " — السعر المقترح لإعلانك: " + Math.round(avg * areaSqm).toLocaleString("en-US") + " ج.م";
            }
            if (areaSqm > 0 && price > 0) {
                text += " — سعر متر إعلانك: " + Math.round(price / areaSqm).toLocaleString("en-US") + " ج.م";
            }
            hint.textContent = text;
        }
        if (avgCache[area] !== undefined) { render(avgCache[area]); return; }
        fetch("/Listings/AreaAverage?area=" + encodeURIComponent(area))
            .then(function (r) { return r.json(); })
            .then(function (data) { avgCache[area] = data.avgPerSqm || 0; render(avgCache[area]); })
            .catch(function () { hint.textContent = ""; });
    }
    form.addEventListener("input", updateCompleteness);
    form.addEventListener("change", function () { updateCompleteness(); updateSuggestedPrice(); });
    updateCompleteness();
    updateSuggestedPrice();
});

document.addEventListener("DOMContentLoaded", function () {
    // ظل الهيدر عند السكرول + هيدر شفاف فوق الهيرو (وضع داكن بس).
    var header = document.querySelector(".site-header");
    if (header) {
        var hasHero = !!document.querySelector(".hero");
        function updateHeader() {
            header.classList.toggle("scrolled", window.scrollY > 10);
            var isLight = document.documentElement.getAttribute("data-theme") === "light";
            header.classList.toggle("transparent", hasHero && !isLight && window.scrollY <= 10);
        }
        window.addEventListener("scroll", updateHeader, { passive: true });
        updateHeader();
    }
    var backToTop = document.getElementById("backToTop");
    if (!backToTop) { return; }
    var ringFg = backToTop.querySelector(".ring-fg");
    function updateBackToTop() {
        backToTop.classList.toggle("visible", window.scrollY > 200);
        if (ringFg) {
            var max = document.documentElement.scrollHeight - window.innerHeight;
            var p = max > 0 ? Math.min(window.scrollY / max, 1) : 0;
            ringFg.style.strokeDashoffset = (100.5 * (1 - p)).toFixed(1);
        }
    }
    window.addEventListener("scroll", updateBackToTop, { passive: true });
    updateBackToTop();
    backToTop.addEventListener("click", function () {
        window.scrollTo({ top: 0, behavior: "smooth" });
    });
});

// اقتراحات أثناء الكتابة في مربع البحث (مناطق + عناوين إعلانات).
document.addEventListener("DOMContentLoaded", function () {
    var input = document.getElementById("searchInput");
    var box = document.getElementById("searchSuggest");
    if (!input || !box) { return; }
    var timer = null;
    input.addEventListener("input", function () {
        var term = input.value.trim();
        clearTimeout(timer);
        if (term.length < 2) { box.innerHTML = ""; box.style.display = "none"; return; }
        timer = setTimeout(function () {
            fetch("/Listings/Suggest?term=" + encodeURIComponent(term))
                .then(function (r) { return r.json(); })
                .then(function (items) {
                    if (!items.length) { box.innerHTML = ""; box.style.display = "none"; return; }
                    box.innerHTML = "";
                    items.forEach(function (item) {
                        var a = document.createElement("a");
                        a.href = item.url;
                        a.className = "suggest-item";
                        a.textContent = (item.kind === "area" ? "📍 " : "🏠 ") + item.label;
                        box.appendChild(a);
                    });
                    box.style.display = "block";
                })
                .catch(function () { box.style.display = "none"; });
        }, 250);
    });
    input.addEventListener("blur", function () { setTimeout(function () { box.style.display = "none"; }, 150); });
    input.addEventListener("focus", function () { if (box.children.length) { box.style.display = "block"; } });
});

// Batch 20 — design 2: Toast عائم + تبديل صور الكارت عند الـ hover.
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".toast").forEach(function (t) {
        setTimeout(function () { t.classList.add("hide"); }, 6000);
        var btn = t.querySelector(".toast-close");
        if (btn) { btn.addEventListener("click", function () { t.classList.add("hide"); }); }
    });
    document.querySelectorAll(".card-photos").forEach(function (box) {
        var imgs = box.querySelectorAll("img");
        if (imgs.length < 2) { return; }
        var card = box.closest(".listing-card");
        if (!card) { return; }
        var i = 0, timer = null;
        function show(n) {
            imgs[i].classList.remove("active");
            i = (n + imgs.length) % imgs.length;
            imgs[i].classList.add("active");
        }
        card.addEventListener("mouseenter", function () {
            timer = setInterval(function () { show(i + 1); }, 1200);
        });
        card.addEventListener("mouseleave", function () {
            clearInterval(timer);
            show(0);
        });
        card.querySelectorAll(".card-photo-nav").forEach(function (btn) {
            btn.addEventListener("click", function (event) {
                event.stopPropagation();
                show(i + parseInt(btn.getAttribute("data-dir"), 10));
            });
        });
    });
});

// Batch 21 — design 3: تبويبات التفاصيل + تحميل ضبابي للصور.
document.addEventListener("DOMContentLoaded", function () {
    var tabButtons = document.querySelectorAll(".details-tab-btn");
    if (tabButtons.length < 2) {
        var row = document.querySelector(".details-tab-buttons");
        if (row) { row.hidden = true; }
    } else {
        tabButtons.forEach(function (btn) {
            btn.addEventListener("click", function () {
                tabButtons.forEach(function (b) { b.classList.remove("active"); });
                document.querySelectorAll(".details-tab-panel").forEach(function (p) { p.hidden = true; p.classList.remove("active"); });
                btn.classList.add("active");
                var panel = document.getElementById(btn.getAttribute("data-tab"));
                if (panel) { panel.hidden = false; panel.classList.add("active"); }
            });
        });
    }
    document.querySelectorAll("img.listing-photo, img.gallery-main-photo").forEach(function (img) {
        if (img.complete && img.naturalWidth > 0) { img.classList.add("loaded"); }
        else { img.addEventListener("load", function () { img.classList.add("loaded"); }); }
    });
});

// Batch 22 — design 4: تمييز الاختلافات في المقارنة + إزالة مفضلة الضيف.
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".compare-table tbody tr").forEach(function (row) {
        var cells = Array.prototype.slice.call(row.querySelectorAll("td"));
        if (cells.length < 2) { return; }
        var first = cells[0].textContent.trim();
        var allSame = cells.every(function (c) { return c.textContent.trim() === first; });
        if (!allSame) { row.classList.add("row-diff"); }
    });
    document.querySelectorAll(".guest-favorite-remove").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var id = parseInt(btn.getAttribute("data-id"), 10);
            var ids = [];
            try { ids = JSON.parse(localStorage.getItem("aqarak-guest-favorites") || "[]"); } catch (e) {}
            ids = ids.filter(function (n) { return n !== id; });
            try { localStorage.setItem("aqarak-guest-favorites", JSON.stringify(ids)); } catch (e) {}
            document.cookie = "aqarak-guest-favs=" + ids.join(",") + "; path=/; max-age=" + (365 * 24 * 60 * 60);
            window.location.reload();
        });
    });
});

// Batch 24 — design 6 (29): أرقام متحركة في إحصائيات الرئيسية.
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".count-up").forEach(function (el) {
        var target = parseInt(el.textContent.trim(), 10);
        if (isNaN(target) || target <= 0) { return; }
        var start = null;
        function tick(ts) {
            if (!start) { start = ts; }
            var p = Math.min((ts - start) / 900, 1);
            el.textContent = Math.round(target * p);
            if (p < 1) { requestAnimationFrame(tick); }
        }
        requestAnimationFrame(tick);
    });
});

// Batch 24 — design 6 (47): تبويبات لوحة الأدمن.
document.addEventListener("DOMContentLoaded", function () {
    var tabs = document.querySelectorAll(".dash-tab-btn");
    if (!tabs.length) { return; }
    var params = new URLSearchParams(window.location.search);
    var initial = (params.get("userId") || params.get("area")) ? "dash-listings" : "dash-overview";
    function showTab(id) {
        document.querySelectorAll(".dash-tab").forEach(function (s) { s.hidden = s.id !== id; });
        tabs.forEach(function (b) { b.classList.toggle("active", b.getAttribute("data-tab") === id); });
    }
    tabs.forEach(function (b) {
        b.addEventListener("click", function () { showTab(b.getAttribute("data-tab")); });
    });
    showTab(initial);
});

// Batch 24 — design 6 (43): سحب وإفلات ملف CSV في الاستيراد.
document.addEventListener("DOMContentLoaded", function () {
    var zone = document.getElementById("csvDropZone");
    if (!zone) { return; }
    var input = zone.querySelector('input[type="file"]');
    ["dragover", "dragenter"].forEach(function (ev) {
        zone.addEventListener(ev, function (e) { e.preventDefault(); zone.classList.add("drag-over"); });
    });
    ["dragleave", "drop"].forEach(function (ev) {
        zone.addEventListener(ev, function (e) { e.preventDefault(); zone.classList.remove("drag-over"); });
    });
    zone.addEventListener("drop", function (e) {
        var files = e.dataTransfer && e.dataTransfer.files;
        if (files && files.length) { input.files = files; }
    });
});

// Batch 25 — design 7
document.addEventListener("DOMContentLoaded", function () {
    // 13 — علامة * حمراء على حقول الإجباري.
    document.querySelectorAll("form.form label").forEach(function (label) {
        var next = label.nextElementSibling;
        if (next && next.hasAttribute && next.hasAttribute("data-val-required")) {
            label.classList.add("required-label");
        }
    });
    // 15/16 — عداد حروف للعنوان والوصف.
    function bindCounter(inputId, counterId, max) {
        var input = document.getElementById(inputId);
        var counter = document.getElementById(counterId);
        if (!input || !counter) { return; }
        function update() { counter.textContent = input.value.length + " / " + max; }
        input.addEventListener("input", update);
        update();
    }
    bindCounter("Title", "titleCounter", 200);
    bindCounter("Description", "descCounter", 2000);
    // 19 — معاينة الفيديو قبل الرفع.
    var videoInput = document.getElementById("VideoFile");
    var videoPreview = document.getElementById("videoPreview");
    if (videoInput && videoPreview) {
        videoInput.addEventListener("change", function () {
            videoPreview.innerHTML = "";
            var file = videoInput.files && videoInput.files[0];
            if (!file) { return; }
            var v = document.createElement("video");
            v.controls = true;
            v.src = URL.createObjectURL(file);
            videoPreview.appendChild(v);
        });
    }
    // 34 — ترتيب النتائج كأزرار: الشريحة تضبط القائمة المخفية وتبعت النموذج.
    document.querySelectorAll(".sort-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            var form = chip.closest("form");
            var select = form && form.querySelector('select[name="sort"]');
            if (select) { select.value = chip.getAttribute("data-sort"); }
            if (form) { form.submit(); }
        });
    });
});

// Batch 26 — design 8: خطوة مراجعة + تنسيق السعر + تنقل الخطوات + شريط التقدم + فلتر أحدث الإعلانات + عدد الفلاتر + معاينة التنبيه.
document.addEventListener("DOMContentLoaded", function () {
    var form = document.getElementById("listingForm");
    // 18 — خطوة مراجعة قبل حفظ الإعلان.
    var reviewPanel = document.getElementById("reviewPanel");
    function renderReview() {
        if (!form || !reviewPanel) { return; }
        var title = document.getElementById("Title");
        var price = document.getElementById("Price");
        var areaSqm = document.getElementById("AreaSqm");
        var type = document.getElementById("PropertyType");
        var area = document.getElementById("Area");
        var newPhotos = document.querySelectorAll("#photoPreview .photo-preview-item").length;
        var existingPhotos = document.querySelectorAll("#existingPhotos li").length;
        reviewPanel.innerHTML = ""
            + "<p><strong>العنوان:</strong> " + (title ? title.value : "") + "</p>"
            + "<p><strong>السعر:</strong> " + (price && price.value ? Number(price.value).toLocaleString("en-US") + " جنيه" : "-") + "</p>"
            + "<p><strong>المساحة:</strong> " + (areaSqm && areaSqm.value ? areaSqm.value + " م²" : "-") + "</p>"
            + "<p><strong>النوع:</strong> " + (type ? type.options[type.selectedIndex].text : "") + "</p>"
            + "<p><strong>المنطقة:</strong> " + (area ? area.options[area.selectedIndex].text : "") + "</p>"
            + "<p><strong>عدد الصور:</strong> " + (newPhotos + existingPhotos) + "</p>";
    }
    if (form && reviewPanel) {
        form.addEventListener("input", renderReview);
        form.addEventListener("change", renderReview);
        renderReview();
    }
    // 20 — السعر بتنسيق آلاف تلقائي (قراءة فقط تحت الحقل).
    var priceInput = document.getElementById("Price");
    var priceFormatted = document.getElementById("priceFormatted");
    if (priceInput && priceFormatted) {
        function updatePriceFormatted() {
            var v = parseFloat(priceInput.value);
            priceFormatted.textContent = isNaN(v) ? "" : v.toLocaleString("en-US") + " جنيه";
        }
        priceInput.addEventListener("input", updatePriceFormatted);
        updatePriceFormatted();
    }
    // 23 — أزرار التالي/السابق بين خطوات النموذج.
    document.querySelectorAll(".step-next, .step-prev").forEach(function (btn) {
        btn.addEventListener("click", function () {
            if (window.__wizardActive) { return; }
            var target = document.querySelector(".form-step." + btn.getAttribute("data-target"));
            if (target) { target.scrollIntoView({ behavior: "smooth", block: "start" }); }
        });
    });
    // 24 — شريط تقدم الخطوات.
    var stepsBar = document.getElementById("stepsProgressBar");
    var stepHeads = document.querySelectorAll(".form-step");
    function updateStepsProgress() {
        if (window.__wizardActive) { return; }
        if (!stepsBar || !stepHeads.length) { return; }
        var current = 0;
        stepHeads.forEach(function (h, i) {
            if (h.getBoundingClientRect().top < 200) { current = i; }
        });
        stepsBar.style.width = ((current + 1) / stepHeads.length * 100) + "%";
    }
    if (stepsBar) {
        window.addEventListener("scroll", updateStepsProgress, { passive: true });
        updateStepsProgress();
    }
    // 33 — فلتر سريع (بيع/إيجار) على سلايدر أحدث الإعلانات.
    var latestSlider = document.getElementById("latestSlider");
    document.querySelectorAll(".latest-filter .sort-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            document.querySelectorAll(".latest-filter .sort-chip").forEach(function (c) { c.classList.remove("active"); });
            chip.classList.add("active");
            var filter = chip.getAttribute("data-filter");
            if (!latestSlider) { return; }
            latestSlider.querySelectorAll(".listing-card").forEach(function (card) {
                card.style.display = (filter === "All" || card.getAttribute("data-listing-type") === filter) ? "" : "none";
            });
        });
    });
    // 37 — عدد الفلاتر المفعّلة على عنوان الفلاتر.
    var filtersBox = document.querySelector(".filters-box");
    if (filtersBox) {
        var summary = filtersBox.querySelector("summary");
        var filterForm = filtersBox.querySelector(".filter-form");
        if (summary && filterForm) {
            var activeCount = 0;
            filterForm.querySelectorAll("input, select").forEach(function (el) {
                if (el.name === "sort" || el.type === "submit" || el.type === "button") { return; }
                if (el.type === "checkbox") { if (el.checked) { activeCount++; } }
                else if (el.value) { activeCount++; }
            });
            if (activeCount > 0) {
                var badge = document.createElement("span");
                badge.className = "filter-count";
                badge.textContent = activeCount;
                summary.appendChild(badge);
            }
        }
    }
    // 47 — معاينة حية لشكل التنبيه.
    var notifTitle = document.getElementById("Title");
    var notifMessage = document.getElementById("Message");
    var previewTitle = document.getElementById("previewTitle");
    var previewMessage = document.getElementById("previewMessage");
    if (notifTitle && previewTitle) {
        notifTitle.addEventListener("input", function () { previewTitle.textContent = notifTitle.value || "عنوان التنبيه"; });
    }
    if (notifMessage && previewMessage) {
        notifMessage.addEventListener("input", function () { previewMessage.textContent = notifMessage.value || "نص التنبيه"; });
    }
});

// Batch 27 — design 9
document.addEventListener("DOMContentLoaded", function () {
    // 50 — toast helper used by the copy handler.
    window.showToast = function (message) {
        var toast = document.createElement("div");
        toast.className = "toast toast-info";
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.classList.add("hide");
            setTimeout(function () { toast.remove(); }, 400);
        }, 2500);
    };

    // 16 — word counter for the description.
    var desc = document.getElementById("Description");
    var wordCounter = document.getElementById("descWordCounter");
    if (desc && wordCounter) {
        var updateWords = function () {
            var count = desc.value.trim() === "" ? 0 : desc.value.trim().split(/\s+/).length;
            wordCounter.textContent = count + " كلمة";
        };
        desc.addEventListener("input", updateWords);
        updateWords();
    }

    // 17 — confirm before saving a listing.
    var listingForm = document.getElementById("listingForm");
    if (listingForm) {
        var saveBtn = listingForm.querySelector('button[type="submit"]');
        if (saveBtn) {
            saveBtn.addEventListener("click", function (e) {
                if (!confirm("حفظ الإعلان؟")) {
                    e.preventDefault();
                }
            });
        }
    }

    // 18 — morning/evening chips in the viewing form.
    document.querySelectorAll(".time-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            var input = document.getElementById("requestedAtInput");
            if (!input) { return; }
            var datePart = input.value ? input.value.split("T")[0] : new Date().toISOString().slice(0, 10);
            input.value = datePart + "T" + chip.getAttribute("data-time");
        });
    });

    // 19 — copy from a previous listing (Create only).
    var copySelect = document.getElementById("copyFromListing");
    if (copySelect) {
        copySelect.addEventListener("change", function () {
            var opt = copySelect.options[copySelect.selectedIndex];
            if (!opt || !opt.value) { return; }
            var setValue = function (id, value) {
                var el = document.getElementById(id);
                if (el) {
                    el.value = value || "";
                    el.dispatchEvent(new Event("input", { bubbles: true }));
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                    return;
                }
                var radio = document.querySelector("input[name='" + id + "'][value='" + value + "']");
                if (radio) {
                    radio.checked = true;
                    radio.dispatchEvent(new Event("change", { bubbles: true }));
                }
            };
            setValue("Title", opt.getAttribute("data-title"));
            setValue("Price", opt.getAttribute("data-price"));
            setValue("AreaSqm", opt.getAttribute("data-area"));
            setValue("Area", opt.getAttribute("data-area-name"));
            setValue("ListingType", opt.getAttribute("data-listing-type"));
            setValue("PropertyType", opt.getAttribute("data-property-type"));
            setValue("Description", opt.getAttribute("data-desc"));
        });
    }

    // 21 — compact view in MyListings.
    var compactToggle = document.getElementById("compactToggle");
    var myGrid = document.getElementById("myListingsGrid");
    if (compactToggle && myGrid) {
        compactToggle.addEventListener("click", function () {
            var compact = myGrid.classList.toggle("my-listings-compact");
            compactToggle.textContent = compact ? "عرض كروت" : "عرض مدمج";
        });
    }

    // 26 — register two steps.
    var regNext = document.getElementById("registerNextBtn");
    var regPrev = document.getElementById("registerPrevBtn");
    var regStep1 = document.getElementById("registerStep1");
    var regStep2 = document.getElementById("registerStep2");
    if (regNext && regPrev && regStep1 && regStep2) {
        regNext.addEventListener("click", function () { regStep1.hidden = true; regStep2.hidden = false; });
        regPrev.addEventListener("click", function () { regStep2.hidden = true; regStep1.hidden = false; });
        var step2Errors = regStep2.querySelectorAll(".field-error:not(:empty)");
        if (step2Errors.length > 0) { regStep1.hidden = true; regStep2.hidden = false; }
    }

    // 31 — quick area chips set minArea.
    document.querySelectorAll(".area-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            var minArea = document.querySelector('input[name="minArea"]');
            if (minArea) { minArea.value = chip.getAttribute("data-min"); }
        });
    });

    // 32 — load more listings instead of pagination.
    var loadMoreBtn = document.getElementById("loadMoreBtn");
    if (loadMoreBtn) {
        loadMoreBtn.addEventListener("click", function () {
            var nextPage = parseInt(loadMoreBtn.getAttribute("data-next-page"), 10);
            var totalPages = parseInt(loadMoreBtn.getAttribute("data-total-pages"), 10);
            var url = new URL(window.location.href);
            var areaMatch = url.pathname.match(/\/Listings\/Area\/(.+)$/);
            if (areaMatch) {
                url.pathname = "/Listings";
                url.searchParams.set("area", decodeURIComponent(areaMatch[1]));
            }
            url.searchParams.set("page", nextPage);
            loadMoreBtn.disabled = true;
            loadMoreBtn.textContent = "جاري التحميل...";
            var grid = document.querySelector(".cards-grid");
            var skeletons = [];
            if (grid) {
                for (var s = 0; s < 3; s++) {
                    var sk = document.createElement("div");
                    sk.className = "skeleton-card";
                    grid.appendChild(sk);
                    skeletons.push(sk);
                }
            }
            fetch(url.toString())
                .then(function (r) { return r.text(); })
                .then(function (html) {
                    skeletons.forEach(function (sk) { sk.remove(); });
                    var doc = new DOMParser().parseFromString(html, "text/html");
                    var grid = document.querySelector(".cards-grid");
                    var newGrid = doc.querySelector(".cards-grid");
                    if (grid && newGrid) {
                        newGrid.querySelectorAll(":scope > .listing-card").forEach(function (el) {
                            grid.appendChild(el);
                            el.classList.add("revealed");
                        });
                    }
                    var next = nextPage + 1;
                    if (next > totalPages) {
                        loadMoreBtn.remove();
                    } else {
                        loadMoreBtn.setAttribute("data-next-page", next);
                        loadMoreBtn.disabled = false;
                        loadMoreBtn.textContent = "عرض المزيد ↓";
                    }
                })
                .catch(function () {
                    loadMoreBtn.disabled = false;
                    loadMoreBtn.textContent = "عرض المزيد ↓";
                });
        });
    }

    // 34 — alternate the hero background every 8 seconds.
    var hero = document.querySelector(".hero");
    if (hero) {
        setInterval(function () { hero.classList.toggle("hero-alt"); }, 8000);
    }

    // 36 — user menu dropdown.
    var userMenuBtn = document.getElementById("userMenuBtn");
    var userMenuDropdown = document.getElementById("userMenuDropdown");
    if (userMenuBtn && userMenuDropdown) {
        userMenuBtn.addEventListener("click", function (e) {
            e.stopPropagation();
            userMenuDropdown.hidden = !userMenuDropdown.hidden;
        });
        document.addEventListener("click", function (e) {
            if (!userMenuDropdown.hidden && !e.target.closest(".user-menu")) {
                userMenuDropdown.hidden = true;
            }
        });
    }

    // 49 — page loading indicator on internal links.
    document.addEventListener("click", function (e) {
        var a = e.target.closest("a");
        if (!a || a.target === "_blank") { return; }
        var href = a.getAttribute("href");
        if (!href || href.startsWith("#") || href.startsWith("mailto:") || href.startsWith("tel:") || href.startsWith("http")) { return; }
        var loader = document.getElementById("pageLoader");
        if (loader) { loader.classList.add("loading"); }
    });
});

// Batch 28 — design 10
document.addEventListener("DOMContentLoaded", function () {
    // (44) ظهور تدريجي للكروت عند التمرير.
    var cards = document.querySelectorAll(".listing-card");
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        cards.forEach(function (c) { c.classList.add("revealed"); });
    } else if ("IntersectionObserver" in window) {
        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) { e.target.classList.add("revealed"); observer.unobserve(e.target); }
            });
        }, { threshold: 0.05 });
        cards.forEach(function (c) { observer.observe(c); });
    } else {
        cards.forEach(function (c) { c.classList.add("revealed"); });
    }
    // (12) نسخ الوصف.
    var copyDescBtn = document.getElementById("copyDescriptionBtn");
    if (copyDescBtn) {
        copyDescBtn.addEventListener("click", function () {
            var textEl = document.getElementById("listingDescriptionText");
            var text = textEl ? textEl.textContent : "";
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(function () {
                    if (window.showToast) { window.showToast("تم النسخ ✓"); }
                });
            }
        });
    }
    // (16) اقتراح عنوان تلقائي من نوع العقار والغرف والمنطقة.
    var suggestTitleBtn = document.getElementById("suggestTitleBtn");
    if (suggestTitleBtn) {
        suggestTitleBtn.addEventListener("click", function () {
            var titleInput = document.getElementById("Title");
            if (!titleInput || titleInput.value.trim()) { return; }
            var typeRadio = document.querySelector("input[name='PropertyType']:checked");
            var typeSelect = document.getElementById("PropertyType");
            var roomsInput = document.getElementById("Rooms");
            var areaSelect = document.getElementById("Area");
            var typeText = typeRadio
                ? (typeRadio.closest("label") ? (typeRadio.closest("label").getAttribute("data-text") || "") : "")
                : (typeSelect ? typeSelect.options[typeSelect.selectedIndex].text : "");
            var rooms = roomsInput && roomsInput.value ? roomsInput.value + " غرف في " : "";
            var area = areaSelect ? areaSelect.value : "";
            var suggestion = (typeText + " " + rooms + area).trim();
            if (suggestion) {
                titleInput.value = suggestion;
                titleInput.dispatchEvent(new Event("input", { bubbles: true }));
            }
        });
    }
    // (21) مقارنة كل المفضلة دفعة واحدة (بحد أقصى 3).
    var compareAllBtn = document.getElementById("compareAllBtn");
    if (compareAllBtn) {
        compareAllBtn.addEventListener("click", function () {
            var toggles = document.querySelectorAll(".compare-toggle[data-id]");
            var clicked = 0;
            toggles.forEach(function (btn) {
                if (clicked < 3 && btn.textContent.trim() !== "✓") { btn.click(); clicked++; }
            });
            if (clicked === 0 && window.showToast) { window.showToast("المقارنة ممتلئة (3 كحد أقصى)"); }
        });
    }
    // (25) تمييز الأفضل في كل صف بالمقارنة (الأرخص سعرًا، والأعلى في الباقي).
    var compareTable = document.querySelector(".compare-table");
    if (compareTable) {
        compareTable.querySelectorAll("tbody tr").forEach(function (row) {
            var cells = Array.from(row.querySelectorAll("td"));
            if (cells.length < 2) { return; }
            var values = cells.map(function (td) {
                var num = parseFloat(td.textContent.replace(/[^0-9.]/g, ""));
                return isNaN(num) ? null : num;
            });
            if (values.some(function (v) { return v === null; })) { return; }
            var label = row.querySelector("th") ? row.querySelector("th").textContent : "";
            var best = label.indexOf("السعر") >= 0 ? Math.min.apply(null, values) : Math.max.apply(null, values);
            cells.forEach(function (td, i) { if (values[i] === best) { td.classList.add("best-value"); } });
        });
    }
    // (26) مؤشر قوة كلمة المرور.
    var passwordInput = document.getElementById("Password");
    var strengthBar = document.getElementById("passwordStrengthBar");
    var strengthText = document.getElementById("passwordStrengthText");
    if (passwordInput && strengthBar) {
        passwordInput.addEventListener("input", function () {
            var v = passwordInput.value;
            var score = 0;
            if (v.length >= 8) { score++; }
            if (/[a-z]/.test(v) && /[A-Z]/.test(v)) { score++; }
            if (/\d/.test(v)) { score++; }
            if (/[^a-zA-Z0-9]/.test(v)) { score++; }
            var widths = ["0%", "25%", "50%", "75%", "100%"];
            var colors = ["transparent", "#ef4444", "#f59e0b", "#a3e635", "#22c55e"];
            var texts = ["", "ضعيفة", "متوسطة", "قوية", "ممتازة"];
            strengthBar.style.width = widths[score];
            strengthBar.style.background = colors[score];
            if (strengthText) { strengthText.textContent = texts[score] ? "قوة كلمة المرور: " + texts[score] : ""; }
        });
    }
    // (28) تحديث أحدث الإعلانات.
    var refreshLatestBtn = document.getElementById("refreshLatestBtn");
    if (refreshLatestBtn) {
        refreshLatestBtn.addEventListener("click", function () { window.location.reload(); });
    }
    // (29) عدد النتائج لحظيًا أثناء الفلترة.
    var filterForm = document.querySelector(".filters-box.filter-form");
    var searchInput = document.getElementById("searchInput");
    if (filterForm && searchInput) {
        var liveCount = document.createElement("span");
        liveCount.id = "liveResultsCount";
        searchInput.parentNode.appendChild(liveCount);
        var timer = null;
        filterForm.addEventListener("input", function () {
            if (timer) { clearTimeout(timer); }
            timer = setTimeout(function () {
                var params = new URLSearchParams(new FormData(filterForm));
                params.set("page", "1");
                fetch("/Listings?" + params.toString())
                    .then(function (r) { return r.text(); })
                    .then(function (html) {
                        var doc = new DOMParser().parseFromString(html, "text/html");
                        var bar = doc.querySelector(".results-bar span");
                        liveCount.textContent = bar ? bar.textContent : "";
                    })
                    .catch(function () { liveCount.textContent = ""; });
            }, 600);
        });
    }
    // (33) بحث الموبايل في الهيدر.
    var mobileSearchBtn = document.getElementById("mobileSearchBtn");
    var mobileSearchForm = document.getElementById("mobileSearchForm");
    if (mobileSearchBtn && mobileSearchForm) {
        mobileSearchBtn.addEventListener("click", function () {
            var hidden = mobileSearchForm.hidden;
            mobileSearchForm.hidden = !hidden;
            if (hidden) {
                var input = mobileSearchForm.querySelector("input");
                if (input) { input.focus(); }
            }
        });
    }
    // (46) مؤشر التحميل بنسبة مئوية.
    var pageLoader = document.getElementById("pageLoader");
    if (pageLoader) {
        var pct = document.createElement("span");
        pct.id = "pageLoaderPct";
        pct.className = "page-loader-pct";
        pct.hidden = true;
        pageLoader.after(pct);
        document.querySelectorAll("a[href]").forEach(function (a) {
            a.addEventListener("click", function () {
                var href = a.getAttribute("href");
                if (!href || href.startsWith("#") || href.startsWith("mailto:") || href.startsWith("tel:") || a.target === "_blank" || href.startsWith("http")) { return; }
                var progress = 0;
                pct.hidden = false;
                pct.textContent = "0%";
                var interval = setInterval(function () {
                    progress = Math.min(progress + Math.floor(Math.random() * 12) + 4, 70);
                    pct.textContent = progress + "%";
                    if (progress >= 70) { clearInterval(interval); }
                }, 200);
            });
        });
    }
});

// Batch 29 — design 11 (responsive / mobile / forms)
document.addEventListener("DOMContentLoaded", function () {
    // 22 — نموذج الإضافة كمعالج خطوات فعلي: خطوة واحدة ظاهرة، والتالي يتحقق من الحقول المطلوبة.
    var listingForm = document.getElementById("listingForm");
    if (listingForm) {
        var wizardSteps = Array.prototype.slice.call(listingForm.querySelectorAll(".form-step-details"));
        var wizardIndex = 0;
        var wizardBar = document.getElementById("stepsProgressBar");
        var wizardPills = document.querySelectorAll(".form-step-item");
        function showWizardStep(i) {
            wizardIndex = Math.max(0, Math.min(i, wizardSteps.length - 1));
            wizardSteps.forEach(function (d, j) { d.open = (j === wizardIndex); });
            if (wizardBar) { wizardBar.style.width = ((wizardIndex + 1) / wizardSteps.length * 100) + "%"; }
            wizardPills.forEach(function (p, j) { p.classList.toggle("active", j === wizardIndex); });
            listingForm.scrollIntoView({ behavior: "smooth", block: "start" });
        }
        function validateWizardStep(i) {
            var fields = wizardSteps[i].querySelectorAll("[data-val-required]");
            for (var k = 0; k < fields.length; k++) {
                if (!fields[k].value || !fields[k].value.trim()) {
                    fields[k].focus();
                    if (window.showToast) { window.showToast("أكمل الحقول المطلوبة في الخطوة دي أولًا"); }
                    return false;
                }
            }
            return true;
        }
        if (wizardSteps.length > 1 && window.matchMedia("(max-width: 899px)").matches) {
            // المعالج للموبايل بس — على الديسكتوب الخطوات الأربع ظاهرة في عمودين.
            window.__wizardActive = true;
            showWizardStep(0);
            listingForm.querySelectorAll(".step-next, .step-prev").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var idx = { "step-1": 0, "step-2": 1, "step-3": 2, "step-4": 3 }[btn.getAttribute("data-target")];
                    if (idx === undefined) { return; }
                    if (idx > wizardIndex && !validateWizardStep(wizardIndex)) { return; }
                    showWizardStep(idx);
                });
            });
            wizardPills.forEach(function (p, j) {
                p.addEventListener("click", function () {
                    if (j > wizardIndex && !validateWizardStep(wizardIndex)) { return; }
                    showWizardStep(j);
                });
            });
        }
    }

    // 13 — زر "أضف إعلانك" العائم يختفي عند التمرير لأسفل ويظهر عند التمرير لأعلى.
    var fabCreate = document.querySelector(".fab-create");
    if (fabCreate) {
        var lastScrollY = window.scrollY;
        window.addEventListener("scroll", function () {
            var y = window.scrollY;
            fabCreate.classList.toggle("fab-hidden", y > 200 && y > lastScrollY);
            lastScrollY = y;
        }, { passive: true });
    }

    // 30 — أزرار أيام سريعة في نموذج المعاينة (تحافظ على الوقت المختار، وإلا 10:00).
    var requestedAt = document.getElementById("requestedAtInput");
    document.querySelectorAll(".day-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            if (!requestedAt) { return; }
            var base = requestedAt.value ? new Date(requestedAt.value) : new Date();
            var time = requestedAt.value ? requestedAt.value.slice(11, 16) : "10:00";
            var d = new Date(base.getFullYear(), base.getMonth(), base.getDate() + parseInt(chip.getAttribute("data-days"), 10));
            var pad = function (n) { return String(n).padStart(2, "0"); };
            requestedAt.value = d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) + "T" + time;
            requestedAt.dispatchEvent(new Event("change", { bubbles: true }));
        });
    });

    // 28 — سلايدرات السعر والمساحة في الفلاتر (متزامنة مع حقول الأرقام).
    function bindRangeSlider(sliderId, inputId) {
        var slider = document.getElementById(sliderId);
        var input = document.getElementById(inputId);
        if (!slider || !input) { return; }
        slider.addEventListener("input", function () { input.value = slider.value; });
        input.addEventListener("change", function () {
            var v = parseFloat(input.value);
            if (!isNaN(v)) { slider.value = Math.max(parseFloat(slider.min), Math.min(parseFloat(slider.max), v)); }
        });
    }
    bindRangeSlider("priceMaxSlider", "maxPriceInput");
    bindRangeSlider("areaMaxSlider", "maxAreaInput");

    // 34 — فلتر حالة "إعلاناتي" كأزرار tabs.
    var statusSelect = document.getElementById("myListingsStatusSelect");
    document.querySelectorAll(".status-tabs .tab-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            if (!statusSelect) { return; }
            statusSelect.value = chip.getAttribute("data-status");
            statusSelect.form.submit();
        });
    });

    // 35 — ترتيب المفضلة كأزرار tabs.
    var favSortSelect = document.getElementById("favoritesSortSelect");
    document.querySelectorAll(".fav-sort-tabs .tab-chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
            if (!favSortSelect) { return; }
            favSortSelect.value = chip.getAttribute("data-sort");
            favSortSelect.form.submit();
        });
    });

    // 27 — أزرار + و - للغرف والحمامات والدور وعدد الأسانسيرات.
    document.querySelectorAll(".stepper").forEach(function (stepper) {
        var input = stepper.querySelector("input[type='number']");
        if (!input) { return; }
        stepper.querySelectorAll(".stepper-btn").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var min = parseFloat(input.min);
                var max = parseFloat(input.max);
                var cur = parseFloat(input.value);
                var next = (isNaN(cur) ? 0 : cur) + parseInt(btn.getAttribute("data-step"), 10);
                if (!isNaN(min)) { next = Math.max(min, next); }
                if (!isNaN(max)) { next = Math.min(max, next); }
                input.value = next;
                input.dispatchEvent(new Event("input", { bubbles: true }));
                input.dispatchEvent(new Event("change", { bubbles: true }));
            });
        });
    });

    // 50 — شريط تقدم القراءة أعلى صفحة التفاصيل.
    var readingBar = document.getElementById("readingProgressBar");
    if (readingBar) {
        var updateReading = function () {
            var max = document.documentElement.scrollHeight - window.innerHeight;
            readingBar.style.width = (max > 0 ? (window.scrollY / max * 100) : 0) + "%";
        };
        window.addEventListener("scroll", updateReading, { passive: true });
        updateReading();
    }

    // 4/5 — درج الفلاتر السفلي في الموبايل والتابلت + زر الفلترة العائم.
    var sidebar = document.querySelector(".search-sidebar");
    var filterFab = document.getElementById("filterFab");
    var filterClose = document.getElementById("filterSheetClose");
    if (sidebar && filterFab) {
        filterFab.addEventListener("click", function () { sidebar.classList.toggle("open"); });
    }
    if (sidebar && filterClose) {
        filterClose.addEventListener("click", function () { sidebar.classList.remove("open"); });
    }

    // Batch 33 (4) — سحب ورقة للأسفل يغلقها (الفلاتر + ورقة الكارت).
    function addSheetSwipe(sheet, closeFn) {
        if (!sheet) return;
        var handle = sheet.querySelector(".sheet-handle");
        if (!handle) return;
        var startY = 0, dy = 0, dragging = false;
        handle.addEventListener("touchstart", function (e) {
            if (e.touches.length !== 1) return;
            dragging = true; startY = e.touches[0].clientY; dy = 0;
            sheet.style.transition = "none";
        }, { passive: true });
        handle.addEventListener("touchmove", function (e) {
            if (!dragging) return;
            dy = e.touches[0].clientY - startY;
            if (dy > 0) sheet.style.transform = "translateY(" + dy + "px)";
        }, { passive: true });
        handle.addEventListener("touchend", function () {
            if (!dragging) return;
            dragging = false;
            sheet.style.transition = "";
            sheet.style.transform = "";
            if (dy > 90) closeFn();
        });
    }
    if (sidebar) addSheetSwipe(sidebar, function () { sidebar.classList.remove("open"); });

    /* Batch 33 (11) — أكورديون الفوتر بقى <details> أصلي في _Layout، من غير JS. */

    // Batch 33 (3) — ورقة إجراءات الكارت في الموبايل.
    var cardSheet = document.getElementById("cardSheet");
    var cardSheetBackdrop = document.getElementById("cardSheetBackdrop");
    var cardSheetTitle = document.getElementById("cardSheetTitle");
    var currentCard = null;
    function openCardSheet(card) {
        if (!cardSheet || !card) return;
        currentCard = card;
        var titleEl = card.querySelector(".listing-body h3 a");
        var title = titleEl ? titleEl.textContent.trim() : "";
        var detailsLink = card.querySelector(".view-details-full");
        var shareBtn = card.querySelector(".actions .share-copy");
        var url = shareBtn ? shareBtn.getAttribute("data-url") : (detailsLink ? detailsLink.href : location.href);
        if (cardSheetTitle) cardSheetTitle.textContent = title;
        var whats = document.getElementById("cardSheetWhats");
        if (whats) whats.href = "https://wa.me/?text=" + encodeURIComponent((title ? title + " - " : "") + url);
        var det = document.getElementById("cardSheetDetails");
        if (det && detailsLink) det.href = detailsLink.href;
        cardSheet.classList.add("open");
        if (cardSheetBackdrop) cardSheetBackdrop.classList.add("open");
    }
    function closeCardSheet() {
        if (cardSheet) cardSheet.classList.remove("open");
        if (cardSheetBackdrop) cardSheetBackdrop.classList.remove("open");
        currentCard = null;
    }
    document.addEventListener("click", function (e) {
        var menuBtn = e.target.closest(".card-menu-btn");
        if (menuBtn) openCardSheet(menuBtn.closest(".listing-card"));
    });
    if (cardSheetBackdrop) cardSheetBackdrop.addEventListener("click", closeCardSheet);
    var cardSheetClose = document.getElementById("cardSheetClose");
    if (cardSheetClose) cardSheetClose.addEventListener("click", closeCardSheet);
    document.addEventListener("keydown", function (e) { if (e.key === "Escape") closeCardSheet(); });
    function sheetAction(id, fn) {
        var el = document.getElementById(id);
        if (!el) return;
        el.addEventListener("click", function () {
            if (!currentCard) return;
            fn(currentCard);
            closeCardSheet();
        });
    }
    sheetAction("cardSheetFav", function (card) { var b = card.querySelector(".favorite-btn"); if (b) b.click(); });
    sheetAction("cardSheetCompare", function (card) { var b = card.querySelector(".compare-toggle"); if (b) b.click(); });
    sheetAction("cardSheetCopy", function (card) { var b = card.querySelector(".actions .share-copy"); if (b) b.click(); });
    if (cardSheet) addSheetSwipe(cardSheet, closeCardSheet);
});
