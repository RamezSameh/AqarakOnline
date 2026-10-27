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
