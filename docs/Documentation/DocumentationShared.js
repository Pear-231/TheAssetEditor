const documentationPages = [
    {
        title: 'Getting started',
        fileName: 'GettingStarted.html',
        children: [
            { title: 'Installation', fileName: 'Installation.html' },
            { title: 'Settings', fileName: 'Settings.html' },
            { title: 'Working with projects', fileName: 'WorkingWithProjects.html' },
            { title: 'Camera controls', fileName: 'CameraControls.html' }
        ]
    },
    {
        title: 'Kitbashing',
        fileName: 'Kitbashing.html',
        children: [
            { title: 'Mesh Fitter', fileName: 'MeshFitter.html' },
            { title: 'Pin Tool', fileName: 'PinTool.html' },
            { title: 'Photo Studio', fileName: 'PhotoStudio.html' }
        ]
    }
];

const THEME_STORAGE_KEY = 'ae-docs-theme';
const NAV_SECTION_STORAGE_KEY = 'ae-docs-nav-sections';

applyStoredTheme();

document.addEventListener('DOMContentLoaded', () => {
    initializeDocumentationLayout();
    initializeImageOverlay();
});

function applyStoredTheme() {
    let storedTheme = null;
    try {
        storedTheme = localStorage.getItem(THEME_STORAGE_KEY);
    } catch (error) {
        storedTheme = null;
    }

    if (storedTheme === 'light') {
        document.documentElement.setAttribute('data-theme', 'light');
    } else {
        document.documentElement.removeAttribute('data-theme');
    }
}

function isLightTheme() {
    return document.documentElement.getAttribute('data-theme') === 'light';
}

function setTheme(theme) {
    if (theme === 'light') {
        document.documentElement.setAttribute('data-theme', 'light');
    } else {
        document.documentElement.removeAttribute('data-theme');
    }

    try {
        localStorage.setItem(THEME_STORAGE_KEY, theme);
    } catch (error) {
        // Ignore storage failures (private browsing, blocked storage, etc.)
    }
}

function buildThemeToggle() {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'doc-theme-toggle';

    function refreshLabel() {
        button.textContent = isLightTheme() ? '🌞' : '🌚';
        button.title = isLightTheme() ? 'Switch to dark mode' : 'Switch to light mode';
    }

    refreshLabel();

    button.addEventListener('click', () => {
        setTheme(isLightTheme() ? 'dark' : 'light');
        refreshLabel();
    });

    return button;
}

function initializeDocumentationLayout() {
    if (!document.body || document.body.dataset.documentationLayout === 'true') {
        return;
    }

    const currentPage = getCurrentFileName();
    const originalChildren = Array.from(document.body.childNodes);
    const shell = document.createElement('div');
    shell.className = 'doc-shell';

    const sidebar = buildSidebar(currentPage);
    const content = document.createElement('main');
    content.className = 'doc-content';

    const article = document.createElement('article');
    article.className = 'doc-article';

    for (const child of originalChildren) {
        article.appendChild(child);
    }

    content.appendChild(article);
    shell.append(sidebar, content);
    document.body.replaceChildren(shell);
    document.body.dataset.documentationLayout = 'true';

    const activePage = findPage(currentPage);
    if (activePage) {
        document.title = `AssetEditor Documentation - ${activePage.title}`;
    }

    const breadcrumbs = buildBreadcrumbs(currentPage);
    if (breadcrumbs) {
        article.insertBefore(breadcrumbs, article.firstChild);
    }
}

function buildSidebar(currentPage) {
    const sidebar = document.createElement('aside');
    sidebar.className = 'doc-sidebar';

    const header = document.createElement('div');
    header.className = 'doc-sidebar-header';

    const headerLink = document.createElement('a');
    headerLink.href = 'GettingStarted.html';
    headerLink.className = 'doc-sidebar-logo-link';

    const icon = document.createElement('img');
    icon.src = 'Images/AssetEditor_Icon.png';
    icon.alt = '';
    icon.className = 'doc-sidebar-icon';

    const titleSpan = document.createElement('span');
    titleSpan.textContent = 'AssetEditor Documentation';

    headerLink.append(icon, titleSpan);
    header.appendChild(headerLink);
    sidebar.appendChild(header);

    const nav = document.createElement('nav');
    nav.className = 'doc-nav';
    renderNavigationItems(nav, documentationPages, currentPage, 0);
    sidebar.appendChild(nav);

    const footer = document.createElement('div');
    footer.className = 'doc-sidebar-footer';
    footer.appendChild(buildThemeToggle());
    sidebar.appendChild(footer);

    return sidebar;
}

function renderNavigationItems(container, items, currentPage, level) {
    for (const item of items) {
        if (Array.isArray(item.children)) {
            const hasActiveChild = containsActivePage(item.children, currentPage);
            const isActiveSelf = Boolean(item.fileName) && isCurrentPage(item.fileName, currentPage);

            const section = document.createElement('div');
            section.className = 'doc-nav-section';
            section.style.paddingLeft = `${level * 12}px`;

            if (hasActiveChild || isActiveSelf) {
                section.classList.add('active');
            }

            const chevronButton = document.createElement('button');
            chevronButton.type = 'button';
            chevronButton.className = 'doc-nav-chevron-button';

            chevronButton.innerHTML = '<svg class="doc-nav-chevron" viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><path d="M5 3 L11 8 L5 13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>';

            let titleEl;
            if (item.fileName) {
                titleEl = document.createElement('a');
                titleEl.className = 'doc-nav-section-link';
                titleEl.href = item.fileName;
                if (isActiveSelf) {
                    titleEl.classList.add('current');
                    titleEl.setAttribute('aria-current', 'page');
                }
            } else {
                titleEl = document.createElement('span');
                titleEl.className = 'doc-nav-section-label';
            }
            titleEl.textContent = item.title;

            section.append(chevronButton, titleEl);
            container.appendChild(section);

            const childrenContainer = document.createElement('div');
            childrenContainer.className = 'doc-nav-children';
            container.appendChild(childrenContainer);
            renderNavigationItems(childrenContainer, item.children, currentPage, level + 1);

            const navState = loadNavSectionState();
            const isExpanded = (hasActiveChild || isActiveSelf) ? true : (navState[item.title] ?? false);
            setSectionExpanded(chevronButton, childrenContainer, isExpanded);

            chevronButton.addEventListener('click', () => {
                const expanded = !chevronButton.classList.contains('expanded');
                setSectionExpanded(chevronButton, childrenContainer, expanded);
                const state = loadNavSectionState();
                state[item.title] = expanded;
                saveNavSectionState(state);
            });

            continue;
        }

        const link = document.createElement('a');
        link.className = 'doc-nav-link';
        link.textContent = item.title;
        link.href = item.fileName;
        link.style.paddingLeft = `${level * 12}px`;

        if (isCurrentPage(item.fileName, currentPage)) {
            link.classList.add('current');
            link.setAttribute('aria-current', 'page');
        }

        container.appendChild(link);
    }
}

function containsActivePage(items, currentPage) {
    return items.some(item => {
        if (item.fileName && isCurrentPage(item.fileName, currentPage)) {
            return true;
        }

        if (Array.isArray(item.children)) {
            return containsActivePage(item.children, currentPage);
        }

        return false;
    });
}

function setSectionExpanded(section, childrenContainer, expanded) {
    section.classList.toggle('expanded', expanded);
    childrenContainer.style.display = expanded ? '' : 'none';
}

function loadNavSectionState() {
    try {
        return JSON.parse(localStorage.getItem(NAV_SECTION_STORAGE_KEY)) || {};
    } catch (error) {
        return {};
    }
}

function saveNavSectionState(state) {
    try {
        localStorage.setItem(NAV_SECTION_STORAGE_KEY, JSON.stringify(state));
    } catch (error) {
        // Ignore storage failures (private browsing, blocked storage, etc.)
    }
}

function getCurrentFileName() {
    const path = window.location.pathname || '';
    const parts = path.split('/');
    return (parts[parts.length - 1] || '').toLowerCase();
}

function isCurrentPage(fileName, currentPage) {
    return fileName.toLowerCase() === currentPage;
}

function findPage(currentPage) {
    for (const item of documentationPages) {
        if (item.fileName && isCurrentPage(item.fileName, currentPage)) {
            return item;
        }

        if (Array.isArray(item.children)) {
            const child = item.children.find(entry => isCurrentPage(entry.fileName, currentPage));
            if (child) {
                return child;
            }
        }
    }

    return null;
}

function findPageTrail(items, currentPage, trail) {
    for (const item of items) {
        const nextTrail = [...trail, item];

        if (item.fileName && isCurrentPage(item.fileName, currentPage)) {
            return nextTrail;
        }

        if (Array.isArray(item.children)) {
            const found = findPageTrail(item.children, currentPage, nextTrail);
            if (found) {
                return found;
            }
        }
    }

    return null;
}

function buildBreadcrumbs(currentPage) {
    const trail = findPageTrail(documentationPages, currentPage, []);
    if (!trail || trail.length === 0) {
        return null;
    }

    const nav = document.createElement('nav');
    nav.className = 'doc-breadcrumbs';
    nav.setAttribute('aria-label', 'Breadcrumb');

    const homeLink = document.createElement('a');
    homeLink.href = 'GettingStarted.html';
    homeLink.textContent = 'AssetEditor Documentation';
    nav.appendChild(homeLink);

    trail.forEach((item, index) => {
        const separator = document.createElement('span');
        separator.textContent = '›';
        nav.appendChild(separator);

        const isLast = index === trail.length - 1;
        if (isLast) {
            const current = document.createElement('span');
            current.className = 'doc-breadcrumb-current';
            current.textContent = item.title;
            current.setAttribute('aria-current', 'page');
            nav.appendChild(current);
        } else if (item.fileName) {
            const link = document.createElement('a');
            link.href = item.fileName;
            link.textContent = item.title;
            nav.appendChild(link);
        } else {
            const label = document.createElement('span');
            label.textContent = item.title;
            nav.appendChild(label);
        }
    });

    return nav;
}

function initializeImageOverlay() {
    const docImages = document.querySelectorAll('.doc-image');
    let activeOverlay = null;

    function closeOverlay() {
        if (activeOverlay) {
            activeOverlay.remove();
            activeOverlay = null;
        }

        document.removeEventListener('keydown', onKeyDown);
    }

    function onKeyDown(event) {
        if (event.key === 'Escape') {
            closeOverlay();
        }
    }

    function openOverlay(sourceImage) {
        closeOverlay();

        const overlay = document.createElement('div');
        overlay.style.position = 'fixed';
        overlay.style.inset = '0';
        overlay.style.background = 'rgba(0, 0, 0, 0.92)';
        overlay.style.display = 'flex';
        overlay.style.alignItems = 'center';
        overlay.style.justifyContent = 'center';
        overlay.style.padding = '24px';
        overlay.style.boxSizing = 'border-box';
        overlay.style.zIndex = '2147483647';
        overlay.style.cursor = 'zoom-out';

        const overlayImage = document.createElement('img');
        overlayImage.src = sourceImage.currentSrc || sourceImage.src;
        overlayImage.alt = sourceImage.alt || '';
        overlayImage.style.maxWidth = '100%';
        overlayImage.style.maxHeight = '100%';
        overlayImage.style.objectFit = 'contain';
        overlayImage.style.border = 'none';
        overlayImage.style.boxShadow = '0 8px 30px rgba(0, 0, 0, 0.45)';
        overlayImage.style.background = '#000';
        overlayImage.style.cursor = 'auto';

        overlay.addEventListener('click', closeOverlay);
        overlayImage.addEventListener('click', (event) => {
            event.stopPropagation();
        });

        overlay.appendChild(overlayImage);
        document.body.appendChild(overlay);
        document.addEventListener('keydown', onKeyDown);
        activeOverlay = overlay;
    }

    for (const image of docImages) {
        if (!image.getAttribute('title')) {
            image.setAttribute('title', 'Click to enlarge');
        }

        image.addEventListener('click', () => {
            openOverlay(image);
        });
    }
}
