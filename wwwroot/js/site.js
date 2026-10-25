// سلوكيات واجهة عامة: قائمة الموبايل، قائمة الإشعارات، إظهار كلمة المرور.
// لا يعتمد على أي مكتبة خارجية، ويعمل مع العربية (RTL) والإنجليزية (LTR).
document.addEventListener("DOMContentLoaded", function () {
    // قائمة التنقل في الشاشات الصغيرة.
    var navToggle = document.getElementById("navToggle");
    var mainNav = document.getElementById("mainNav");
    if (navToggle && mainNav) {
        navToggle.addEventListener("click", function () {
            var open = mainNav.classList.toggle("open");
            navToggle.setAttribute("aria-expanded", open ? "true" : "false");
        });
    }

    // قائمة الإشعارات (الجرس): فتح/إغلاق، والإغلاق بالضغط خارجها.
    // بتفويض الأحداث (delegation) حتى تعمل بعد استبدال HTML الجرس لحظيًا.
    document.addEventListener("click", function (event) {
        var bellBtn = event.target.closest("#bellBtn");
        var bellDropdown = document.getElementById("bellDropdown");
        if (bellBtn && bellDropdown) {
            event.stopPropagation();
            var hidden = bellDropdown.hasAttribute("hidden");
            if (hidden) {
                bellDropdown.removeAttribute("hidden");
                bellBtn.setAttribute("aria-expanded", "true");
            } else {
                bellDropdown.setAttribute("hidden", "");
                bellBtn.setAttribute("aria-expanded", "false");
            }
            return;
        }
        if (bellDropdown && !event.target.closest(".bell-wrap")) {
            bellDropdown.setAttribute("hidden", "");
            var btn = document.getElementById("bellBtn");
            if (btn) {
                btn.setAttribute("aria-expanded", "false");
            }
        }
    });

    // التحديث اللحظي: عند إشارة من السيرفر، أعد جلب الجرس أو أرقام الأدمن
    // واستبدلها من غير إعادة تحميل الصفحة.
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
                }
            })
            .catch(function () { /* يبقى القديم حتى التحديث التالي */ });
    }

    function refreshAdminNav() {
        var span = document.getElementById("adminNavLinks");
        if (!span) {
            return;
        }
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
