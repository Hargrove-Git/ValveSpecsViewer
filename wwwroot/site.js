// Closes any open header dropdown (<details class="nav-menu">/"user-menu">) when
// clicking outside of it. Plain DOM manipulation -- no Blazor interactivity required,
// so this works identically on static-SSR pages (e.g. Login) and interactive ones.
document.addEventListener('click', function (e) {
    document.querySelectorAll('details.nav-menu[open], details.user-menu[open]').forEach(function (el) {
        if (!el.contains(e.target)) {
            el.removeAttribute('open');
        }
    });
});
