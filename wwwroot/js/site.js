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
    function setNavOpen(open) {
        if (!mainNav) {
            return;
        }
        mainNav.classList.toggle("open", open);
        if (navToggle) {
            navToggle.setAttribute("aria-expanded", open ? "true" : "false");
            navToggle.textContent = open ? "✕" : "☰";
        }
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
                if (!response.ok) {
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
                    if (!response.ok) {
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
                    if (!response.ok) {
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
            refreshBell();
            refreshAdminNav();
            checkListingsChanged();
        }, 30000);
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
        if (!grid) {
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

    // حقل الدور يظهر في نموذج الإعلان للشقة بس، ويتخفي لغيرها.
    var propertyTypeSelect = document.getElementById("PropertyType");
    var floorField = document.getElementById("floorField");
    function toggleFloorField() {
        if (!propertyTypeSelect || !floorField) {
            return;
        }
        floorField.style.display = propertyTypeSelect.value === "Apartment" ? "" : "none";
    }
    if (propertyTypeSelect && floorField) {
        propertyTypeSelect.addEventListener("change", toggleFloorField);
        toggleFloorField();
    }

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
        compareBar.hidden = false;
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
                item.appendChild(img);
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
            var original = btn.textContent;
            btn.textContent = "تم النسخ ✓";
            btn.disabled = true;
            setTimeout(function () {
                btn.textContent = original;
                btn.disabled = false;
            }, 2000);
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
    try {
        applyTheme(localStorage.getItem(themeKey) || "dark");
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
    btn.addEventListener("click", function () {
        var facts = [];
        if (val("Rooms")) { facts.push("الغرف: " + val("Rooms")); }
        if (val("Bathrooms")) { facts.push("الحمامات: " + val("Bathrooms")); }
        if (val("Floor")) { facts.push("الدور: " + val("Floor")); }
        if (checked("IsFurnished")) { facts.push("مفروش"); }
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
        box.scrollIntoView({ behavior: "smooth", block: "nearest" });
    });
});
