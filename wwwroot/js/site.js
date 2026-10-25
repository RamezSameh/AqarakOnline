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
    var bellBtn = document.getElementById("bellBtn");
    var bellDropdown = document.getElementById("bellDropdown");
    if (bellBtn && bellDropdown) {
        bellBtn.addEventListener("click", function (event) {
            event.stopPropagation();
            var hidden = bellDropdown.hasAttribute("hidden");
            if (hidden) {
                bellDropdown.removeAttribute("hidden");
                bellBtn.setAttribute("aria-expanded", "true");
            } else {
                bellDropdown.setAttribute("hidden", "");
                bellBtn.setAttribute("aria-expanded", "false");
            }
        });
        document.addEventListener("click", function (event) {
            if (!bellDropdown.contains(event.target)) {
                bellDropdown.setAttribute("hidden", "");
                bellBtn.setAttribute("aria-expanded", "false");
            }
        });
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
