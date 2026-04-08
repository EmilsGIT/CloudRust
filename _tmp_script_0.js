
    (() => {
      const tabs = Array.from(document.querySelectorAll('.tab[data-tab]'));
      const panels = Array.from(document.querySelectorAll('.tab-panel[data-panel]'));

      function activateFallbackTab(tabName) {
        tabs.forEach((tab) => {
          tab.classList.toggle('active', tab.dataset.tab === tabName);
        });

        panels.forEach((panel) => {
          panel.classList.toggle('active', panel.dataset.panel === tabName);
        });
      }

      window.__rustplusActivateFallbackTab = activateFallbackTab;

      tabs.forEach((tab) => {
        tab.addEventListener('click', () => {
          activateFallbackTab(tab.dataset.tab);
        });
      });
    })();
  
