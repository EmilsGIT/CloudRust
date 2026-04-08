
    (() => {
    const tabs = document.querySelectorAll('.tab');
    const panels = document.querySelectorAll('.tab-panel');
    const switchIdInput = document.getElementById('switchId');
    const switchNameInput = document.getElementById('switchName');
    const switchForm = document.getElementById('newSwitchForm');
    const switchList = document.getElementById('switchList');
    const fcmStatus = document.getElementById('fcmStatus');
    const switchActionStatus = document.getElementById('switchActionStatus');
    const switchStorageKey = 'rustplus.savedSwitches';
    const monitorStorageKey = 'rustplus.savedStorageMonitors';
    const virtualMonitorStorageKey = 'rustplus.virtualStorageMonitors';
    const virtualMonitorLinksStorageKey = 'rustplus.virtualStorageMonitorLinks';
    const monitorWiringArea = document.getElementById('monitorWiringArea');
    const monitorLinkSvg = document.getElementById('monitorLinkSvg');
    const switchWiringArea = document.getElementById('switchWiringArea');
    const switchLinkSvg = document.getElementById('switchLinkSvg');
    const virtualSwitchForm = document.getElementById('virtualSwitchForm');
    const virtualSwitchNameInput = document.getElementById('virtualSwitchName');
    const virtualSwitchList = document.getElementById('virtualSwitchList');
    const virtualSwitchStorageKey = 'rustplus.virtualSwitches';
    const virtualSwitchLinksStorageKey = 'rustplus.virtualSwitchLinks';
    const virtualSwitchLinkJointsStorageKey = 'rustplus.virtualSwitchLinkJoints';
      const virtualMonitorLinkJointsStorageKey = 'rustplus.virtualMonitorLinkJoints';
      let previewMonitorCursorPoint = null;
      let draftMonitorWiringJoints = [];
    const infoSmallOilRig = document.getElementById('infoSmallOilRig');
    const infoLargeOilRig = document.getElementById('infoLargeOilRig');
    const infoCargoShip = document.getElementById('infoCargoShip');
    const infoChinook47 = document.getElementById('infoChinook47');
    const infoPatrolHelicopter = document.getElementById('infoPatrolHelicopter');
    const infoTravelingVendor = document.getElementById('infoTravelingVendor');
    const infoFootnote = document.getElementById('infoFootnote');
    const teamList = document.getElementById('teamList');
    const teamFootnote = document.getElementById('teamFootnote');
    const mapCanvas = document.getElementById('mapCanvas');
    const mapStatus = document.getElementById('mapStatus');
    const mapDebugToggle = document.getElementById('mapDebugToggle');
    const mapDebugPanel = document.getElementById('mapDebugPanel');
    const mapLegend = document.getElementById('mapLegend');
    const mapFootnote = document.getElementById('mapFootnote');
    const monitorForm = document.getElementById('newMonitorForm');
    const monitorIdInput = document.getElementById('monitorId');
    const monitorNameInput = document.getElementById('monitorName');
    const virtualMonitorForm = document.getElementById('newVirtualMonitorForm');
    const virtualMonitorNameInput = document.getElementById('virtualMonitorName');
    const monitorList = document.getElementById('monitorList');
    const virtualMonitorList = document.getElementById('virtualMonitorList');
    const monitorFcmStatus = document.getElementById('monitorFcmStatus');
    const monitorActionStatus = document.getElementById('monitorActionStatus');
    const mySystemUploadButton = document.getElementById('mySystemUploadButton');
    const mySystemUploadInput = document.getElementById('mySystemUploadInput');
    const mySystemRemoveButton = document.getElementById('mySystemRemoveButton');
    const mySystemDeviceTray = document.getElementById('mySystemDeviceTray');
    const mySystemGridShell = document.querySelector('.my-system-grid-shell');
    const mySystemImageLayer = document.getElementById('mySystemImageLayer');
    const mySystemPlacedItemsLayer = document.getElementById('mySystemPlacedItemsLayer');
    const mySystemMonitorWindow = document.getElementById('mySystemMonitorWindow');
    const mySystemMonitorTitle = document.getElementById('mySystemMonitorTitle');
    const mySystemMonitorSubtitle = document.getElementById('mySystemMonitorSubtitle');
    const mySystemMonitorStatus = document.getElementById('mySystemMonitorStatus');
    const mySystemMonitorGrid = document.getElementById('mySystemMonitorGrid');
    const mySystemMonitorCloseButton = document.getElementById('mySystemMonitorCloseButton');
    const mySystemImageMaxBytes = 20 * 1024 * 1024;
    const mySystemImageDatabaseName = 'rustplus-web-ui';
    const mySystemImageStoreName = 'assets';
    const mySystemImageRecordKey = 'my-system-background';
    const mySystemPlacedDevicesStorageKey = 'rustplus.mySystemPlacedDevices';
    let smallOilRigTicker = null;
    let largeOilRigTicker = null;
    let pendingRemoveButton = null;
    let activeWiringVirtualId = null;
    let previewCursorPoint = null;
    let draftWiringJoints = [];
    let fcmEventSource = null;
    let fcmReconnectTimer = null;
    const mapRefreshIntervalMs = 60000;
    const monitorRefreshIntervalMs = 60000;
    const monitorInterRequestDelayMs = 50;
    const virtualSwitchInteractionDelayMs = 50;
    const vendingOverlayScale = 0.48;
    const vendingOverlayOffsetX = -110;
    const vendingOverlayOffsetY = 60;
    let mapAutoRefreshTimer = null;
    let mapLastRequestedAtMs = 0;
    let mapRequestInFlight = null;
    let latestMapPayload = null;
    const mapImagePromiseCache = new Map();
    let mapRenderContext = null;
    let mapDebugLastClick = null;
    let monitorRefreshInFlight = null;
    let monitorLastBulkRefreshAtMs = 0;
    const monitorLastRefreshById = new Map();
    const monitorItemsById = new Map();
    let activeVirtualMonitorWiringId = null;
    let mySystemDatabasePromise = null;
    let mySystemImageObjectUrl = null;
    let mySystemDragState = null;
    let mySystemSuppressNodeClick = false;
    let activeMySystemMonitorKey = null;
    let switchLinkRenderScheduled = false;
    let monitorLinkRenderScheduled = false;
    const fallbackStorage = new Map();
    if (mapDebugToggle) {
      mapDebugToggle.checked = true;
    }

    function activeIndicator() {
      return '<span class="status-indicator"><span class="status-dot" title="Active"></span><span class="status-text">Active</span></span>';
    }

    function delay(ms) {
      return new Promise((resolve) => setTimeout(resolve, ms));
    }

    function readStoredValue(key) {
      try {
        return localStorage.getItem(key);
      } catch {
        return fallbackStorage.has(key) ? fallbackStorage.get(key) : null;
      }
    }

    function writeStoredValue(key, value) {
      try {
        localStorage.setItem(key, value);
      } catch {
        fallbackStorage.set(key, value);
      }
    }

    function runBootTask(task) {
      try {
        const result = task();
        if (result && typeof result.catch === 'function') {
          result.catch((error) => {
            console.error(error);
          });
        }
        return result;
      } catch (error) {
        console.error(error);
        return null;
      }
    }

    function formatFileSize(bytes) {
      if (!Number.isFinite(bytes) || bytes <= 0) {
        return '0 B';
      }

      const units = ['B', 'KB', 'MB', 'GB'];
      let value = bytes;
      let unitIndex = 0;

      while (value >= 1024 && unitIndex < units.length - 1) {
        value /= 1024;
        unitIndex += 1;
      }

      const precision = unitIndex === 0 ? 0 : 1;
      return `${value.toFixed(precision)} ${units[unitIndex]}`;
    }

    function getMySystemDatabase() {
      if (!('indexedDB' in window)) {
        return Promise.reject(new Error('IndexedDB is not available in this browser.'));
      }

      if (!mySystemDatabasePromise) {
        mySystemDatabasePromise = new Promise((resolve, reject) => {
          const request = window.indexedDB.open(mySystemImageDatabaseName, 1);

          request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(mySystemImageStoreName)) {
              database.createObjectStore(mySystemImageStoreName);
            }
          };

          request.onsuccess = () => resolve(request.result);
          request.onerror = () => reject(request.error || new Error('Failed to open image cache.'));
        });
      }

      return mySystemDatabasePromise;
    }

    async function readMySystemImageRecord() {
      const database = await getMySystemDatabase();

      return new Promise((resolve, reject) => {
        const transaction = database.transaction(mySystemImageStoreName, 'readonly');
        const store = transaction.objectStore(mySystemImageStoreName);
        const request = store.get(mySystemImageRecordKey);

        request.onsuccess = () => resolve(request.result || null);
        request.onerror = () => reject(request.error || new Error('Failed to read cached image.'));
      });
    }

    async function writeMySystemImageRecord(record) {
      const database = await getMySystemDatabase();

      return new Promise((resolve, reject) => {
        const transaction = database.transaction(mySystemImageStoreName, 'readwrite');
        const store = transaction.objectStore(mySystemImageStoreName);
        const request = store.put(record, mySystemImageRecordKey);

        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error || new Error('Failed to cache image.'));
      });
    }

    async function deleteMySystemImageRecord() {
      const database = await getMySystemDatabase();

      return new Promise((resolve, reject) => {
        const transaction = database.transaction(mySystemImageStoreName, 'readwrite');
        const store = transaction.objectStore(mySystemImageStoreName);
        const request = store.delete(mySystemImageRecordKey);

        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error || new Error('Failed to remove cached image.'));
      });
    }

    function syncMySystemControls(hasImage) {
      if (mySystemUploadButton) {
        mySystemUploadButton.hidden = hasImage;
      }

      if (mySystemRemoveButton) {
        mySystemRemoveButton.hidden = !hasImage;
      }
    }

    function applyMySystemImage(record) {
      if (!mySystemImageLayer) {
        return;
      }

      if (mySystemImageObjectUrl) {
        URL.revokeObjectURL(mySystemImageObjectUrl);
        mySystemImageObjectUrl = null;
      }

      if (!record?.blob) {
        mySystemImageLayer.style.backgroundImage = 'none';
        mySystemImageLayer.dataset.hasImage = 'false';
        syncMySystemControls(false);
        return;
      }

      mySystemImageObjectUrl = URL.createObjectURL(record.blob);
      mySystemImageLayer.style.backgroundImage = `url("${mySystemImageObjectUrl}")`;
      mySystemImageLayer.dataset.hasImage = 'true';
      syncMySystemControls(true);
    }

    async function loadMySystemImage() {
      if (!mySystemImageLayer) {
        return;
      }

      try {
        const record = await readMySystemImageRecord();
        if (!record) {
          applyMySystemImage(null);
          return;
        }

        applyMySystemImage(record);
      } catch (error) {
        applyMySystemImage(null);
      }
    }

    async function handleMySystemImageSelection(file) {
      if (!file) {
        return;
      }

      if (!file.type.startsWith('image/')) {
        return;
      }

      if (file.size > mySystemImageMaxBytes) {
        window.alert(`Image is too large. Maximum size is ${formatFileSize(mySystemImageMaxBytes)}.`);
        return;
      }

      const record = {
        name: file.name,
        size: file.size,
        type: file.type,
        savedAt: new Date().toISOString(),
        blob: file
      };

      applyMySystemImage(record);

      try {
        await writeMySystemImageRecord(record);
      } catch (error) {
        window.alert(`Image preview loaded, but cache failed: ${error.message}`);
      }
    }

    function getMySystemAvailableDevices() {
      const switches = getSavedSwitches().map((entry) => ({
        uniqueKey: `switch:${entry.id}`,
        type: 'switch',
        id: String(entry.id),
        name: entry.name || String(entry.id),
        iconUrl: '../Icons/smartswitch.png',
        iconAlt: 'Smart switch'
      }));

      const monitors = getSavedMonitors().map((entry) => ({
        uniqueKey: `monitor:${entry.id}`,
        type: 'monitor',
        id: String(entry.id),
        name: entry.name || String(entry.id),
        iconUrl: '../resources/items/storage.monitor.png',
        iconAlt: 'Storage monitor'
      }));

      return [...switches, ...monitors];
    }

    function getSavedMySystemPlacedDevices() {
      try {
        const raw = readStoredValue(mySystemPlacedDevicesStorageKey);
        if (!raw) {
          return [];
        }

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    function saveMySystemPlacedDevices(items) {
      writeStoredValue(mySystemPlacedDevicesStorageKey, JSON.stringify(items));
    }

    function sanitizeMySystemPlacedDevices() {
      const availableKeys = new Set(getMySystemAvailableDevices().map((device) => device.uniqueKey));
      const items = getSavedMySystemPlacedDevices();
      const sanitized = items.filter((item) => availableKeys.has(item.uniqueKey));

      if (sanitized.length !== items.length) {
        saveMySystemPlacedDevices(sanitized);
      }

      return sanitized;
    }

    function clampMySystemPosition(x, y) {
      const gridRect = mySystemPlacedItemsLayer?.getBoundingClientRect();
      if (!gridRect) {
        return { x, y };
      }

      const maxX = Math.max(0, gridRect.width - 42);
      const maxY = Math.max(0, gridRect.height - 48);

      return {
        x: Math.min(Math.max(0, x), maxX),
        y: Math.min(Math.max(0, y), maxY)
      };
    }

    function upsertMySystemPlacedDevice(nextItem) {
      const items = sanitizeMySystemPlacedDevices();
      const existingIndex = items.findIndex((item) => item.uniqueKey === nextItem.uniqueKey);

      if (existingIndex >= 0) {
        items[existingIndex] = {
          ...items[existingIndex],
          ...nextItem
        };
      } else {
        items.push(nextItem);
      }

      saveMySystemPlacedDevices(items);
      renderMySystemPlacedDevices();
      renderMySystemDeviceTray();
    }

    function removeMySystemPlacedDevice(uniqueKey) {
      const items = sanitizeMySystemPlacedDevices().filter((item) => item.uniqueKey !== uniqueKey);
      saveMySystemPlacedDevices(items);
      if (activeMySystemMonitorKey === uniqueKey) {
        closeMySystemMonitorWindow();
      }
      renderMySystemPlacedDevices();
      renderMySystemDeviceTray();
    }

    function renderMySystemDeviceTray() {
      if (!mySystemDeviceTray) {
        return;
      }

      const availableDevices = getMySystemAvailableDevices();
      const placedKeys = new Set(sanitizeMySystemPlacedDevices().map((item) => item.uniqueKey));
      const trayDevices = availableDevices.filter((device) => !placedKeys.has(device.uniqueKey));

      mySystemDeviceTray.innerHTML = '';

      if (trayDevices.length === 0) {
        const emptyState = document.createElement('div');
        emptyState.className = 'my-system-device-tray-empty';
        emptyState.textContent = 'All saved switches and storage monitors are already placed on the grid.';
        mySystemDeviceTray.appendChild(emptyState);
        return;
      }

      trayDevices.forEach((device) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'my-system-device-chip';
        button.dataset.uniqueKey = device.uniqueKey;
        button.dataset.deviceType = device.type;
        button.dataset.deviceId = device.id;
        button.dataset.deviceName = device.name;
        button.dataset.iconUrl = device.iconUrl;
        button.innerHTML = `
          <img src="${device.iconUrl}" alt="${device.iconAlt}" class="my-system-device-icon" />
          <span class="my-system-device-label">${device.name}</span>`;
        mySystemDeviceTray.appendChild(button);
      });
    }

    function renderMySystemPlacedDevices() {
      if (!mySystemPlacedItemsLayer) {
        return;
      }

      const availableByKey = new Map(getMySystemAvailableDevices().map((device) => [device.uniqueKey, device]));
      const items = sanitizeMySystemPlacedDevices();
      mySystemPlacedItemsLayer.innerHTML = '';

      items.forEach((item) => {
        const device = availableByKey.get(item.uniqueKey);
        if (!device) {
          return;
        }

        const element = document.createElement('div');
        element.className = 'my-system-node';
        element.dataset.uniqueKey = item.uniqueKey;
        if (item.uniqueKey === activeMySystemMonitorKey) {
          element.classList.add('is-expanded');
        }
        element.style.left = `${item.x}px`;
        element.style.top = `${item.y}px`;
        element.innerHTML = `
          <button type="button" class="my-system-node-remove" data-remove-node="${item.uniqueKey}" title="Remove from grid">x</button>
          <img src="${device.iconUrl}" alt="${device.iconAlt}" class="my-system-node-icon" draggable="false" />
          <span class="my-system-node-label">${device.name}</span>`;
        mySystemPlacedItemsLayer.appendChild(element);
      });
    }

    function getMySystemDeviceFromElement(element) {
      if (!element) {
        return null;
      }

      return {
        uniqueKey: element.dataset.uniqueKey,
        type: element.dataset.deviceType,
        id: element.dataset.deviceId,
        name: element.dataset.deviceName,
        iconUrl: element.dataset.iconUrl
      };
    }

    function findMySystemGridPosition(clientX, clientY) {
      const gridRect = mySystemPlacedItemsLayer?.getBoundingClientRect();
      if (!gridRect) {
        return null;
      }

      const rawX = clientX - gridRect.left - 21;
      const rawY = clientY - gridRect.top - 17;
      return clampMySystemPosition(rawX, rawY);
    }

    function handleMySystemPointerDown(event) {
      const trayChip = event.target.closest('.my-system-device-chip');
      const placedNode = event.target.closest('.my-system-node');

      if (!trayChip && !placedNode) {
        return;
      }

      if (event.target.closest('.my-system-node-remove')) {
        return;
      }

      const sourceElement = trayChip || placedNode;
      const device = trayChip ? getMySystemDeviceFromElement(trayChip) : {
        uniqueKey: placedNode.dataset.uniqueKey,
        ...getMySystemAvailableDevices().find((entry) => entry.uniqueKey === placedNode.dataset.uniqueKey)
      };

      if (!device?.uniqueKey) {
        return;
      }

      event.preventDefault();

      const ghost = document.createElement('div');
      ghost.className = 'my-system-drag-ghost';
      ghost.innerHTML = `
        <img src="${device.iconUrl}" alt="" class="my-system-device-icon" />
        <span class="my-system-device-label">${device.name}</span>`;
      document.body.appendChild(ghost);

      mySystemDragState = {
        mode: trayChip ? 'new' : 'move',
        uniqueKey: device.uniqueKey,
        device,
        pointerId: event.pointerId,
        ghost,
        startClientX: event.clientX,
        startClientY: event.clientY,
        moved: false,
        offsetX: trayChip ? 21 : event.clientX - placedNode.getBoundingClientRect().left,
        offsetY: trayChip ? 17 : event.clientY - placedNode.getBoundingClientRect().top
      };

      sourceElement.setPointerCapture?.(event.pointerId);
      updateMySystemDragGhost(event.clientX, event.clientY);
    }

    function updateMySystemDragGhost(clientX, clientY) {
      if (!mySystemDragState?.ghost) {
        return;
      }

      if (!mySystemDragState.moved) {
        const deltaX = Math.abs(clientX - mySystemDragState.startClientX);
        const deltaY = Math.abs(clientY - mySystemDragState.startClientY);
        if (deltaX > 4 || deltaY > 4) {
          mySystemDragState.moved = true;
        }
      }

      mySystemDragState.ghost.style.left = `${clientX - mySystemDragState.offsetX}px`;
      mySystemDragState.ghost.style.top = `${clientY - mySystemDragState.offsetY}px`;
    }

    function finishMySystemDrag(clientX, clientY) {
      if (!mySystemDragState) {
        return;
      }

      const wasMoved = mySystemDragState.moved;
      const dragMode = mySystemDragState.mode;
      const uniqueKey = mySystemDragState.uniqueKey;

      if (mySystemDragState.moved) {
        mySystemSuppressNodeClick = true;
        window.setTimeout(() => {
          mySystemSuppressNodeClick = false;
        }, 0);
      }

      const position = findMySystemGridPosition(clientX, clientY);
      if (position && wasMoved) {
        upsertMySystemPlacedDevice({
          uniqueKey: mySystemDragState.uniqueKey,
          type: mySystemDragState.device.type,
          id: mySystemDragState.device.id,
          x: position.x,
          y: position.y
        });
      }

      mySystemDragState.ghost.remove();
      mySystemDragState = null;

      if (!wasMoved && dragMode === 'move' && uniqueKey.startsWith('monitor:')) {
        const monitorId = uniqueKey.slice('monitor:'.length);
        const node = mySystemPlacedItemsLayer?.querySelector(`.my-system-node[data-unique-key="${uniqueKey}"]`);
        openMonitorFromMySystem(monitorId, node);
      }
    }

    function cancelMySystemDrag() {
      if (!mySystemDragState) {
        return;
      }

      mySystemDragState.ghost.remove();
      mySystemDragState = null;
    }

    function positionMySystemMonitorWindow(node) {
      if (!mySystemMonitorWindow || !mySystemGridShell || !node) {
        return;
      }

      const shellRect = mySystemGridShell.getBoundingClientRect();
      const nodeRect = node.getBoundingClientRect();
      const windowRect = mySystemMonitorWindow.getBoundingClientRect();
      const margin = 12;

      let left = nodeRect.left - shellRect.left + nodeRect.width + margin;
      let top = nodeRect.top - shellRect.top - 8;

      if (left + windowRect.width > shellRect.width - margin) {
        left = nodeRect.left - shellRect.left - windowRect.width - margin;
      }

      if (left < margin) {
        left = Math.max(margin, Math.min(shellRect.width - windowRect.width - margin, nodeRect.left - shellRect.left - ((windowRect.width - nodeRect.width) / 2)));
        top = nodeRect.bottom - shellRect.top + margin;
      }

      if (top + windowRect.height > shellRect.height - margin) {
        top = shellRect.height - windowRect.height - margin;
      }

      if (top < margin) {
        top = margin;
      }

      mySystemMonitorWindow.style.left = `${Math.round(left)}px`;
      mySystemMonitorWindow.style.top = `${Math.round(top)}px`;
    }

    function closeMySystemMonitorWindow() {
      if (!mySystemMonitorWindow) {
        return;
      }

      activeMySystemMonitorKey = null;
      mySystemMonitorWindow.style.visibility = 'hidden';
      mySystemMonitorWindow.style.left = '-9999px';
      mySystemMonitorWindow.style.top = '-9999px';
      mySystemMonitorWindow.hidden = true;
      renderMySystemPlacedDevices();
    }

    async function openMonitorFromMySystem(monitorId, node) {
      if (!monitorId) {
        return;
      }

      activeMySystemMonitorKey = `monitor:${monitorId}`;
      renderMySystemPlacedDevices();

      const monitorEntry = getSavedMonitors().find((entry) => String(entry.id) === String(monitorId));
      if (mySystemMonitorTitle) {
        mySystemMonitorTitle.textContent = monitorEntry?.name || 'Storage Monitor';
      }
      if (mySystemMonitorSubtitle) {
        mySystemMonitorSubtitle.textContent = `ID: ${monitorId}`;
      }
      if (mySystemMonitorStatus) {
        mySystemMonitorStatus.textContent = 'Loading monitor...';
      }

      const cachedItems = monitorItemsById.get(String(monitorId));
      if (mySystemMonitorGrid) {
        if (Array.isArray(cachedItems) && cachedItems.length > 0) {
          renderItemSlots(mySystemMonitorGrid, cachedItems, 48);
        } else {
          mySystemMonitorGrid.innerHTML = '';
        }
      }

      if (mySystemMonitorWindow) {
        mySystemMonitorWindow.style.visibility = 'hidden';
        mySystemMonitorWindow.hidden = false;
        positionMySystemMonitorWindow(node);
        mySystemMonitorWindow.style.visibility = 'visible';
      }

      try {
        await loadMonitorItems(String(monitorId), { force: true });
        const items = monitorItemsById.get(String(monitorId)) || [];
        if (mySystemMonitorGrid) {
          renderItemSlots(mySystemMonitorGrid, items, 48);
        }
        if (mySystemMonitorStatus) {
          mySystemMonitorStatus.textContent = `Items: ${items.length}`;
        }
      } catch (error) {
        if (mySystemMonitorStatus) {
          mySystemMonitorStatus.textContent = `Failed to load storage monitor ${monitorId}: ${error.message}`;
        }
      }

      const refreshedNode = mySystemPlacedItemsLayer?.querySelector(`.my-system-node[data-unique-key="monitor:${monitorId}"]`);
      if (refreshedNode) {
        positionMySystemMonitorWindow(refreshedNode);
      }
    }

    function formatEventValue(item) {
      if (!item) {
        return 'No recent activity seen';
      }

      if (item.status === 'Active now' || item.timeSinceLastActive === '0m (active now)') {
        return activeIndicator();
      }

      return item.timeSinceLastActive || 'No recent activity seen';
    }

    function extractEntityId(payload) {
      const candidates = [
        payload?.entityId,
        payload?.id,
        payload?.entityID,
        payload?.entity_id,
        payload?.data?.entityId,
        payload?.data?.id,
        payload?.data
      ];

      for (const candidate of candidates) {
        if (typeof candidate === 'bigint') {
          return candidate.toString();
        }

        if (typeof candidate === 'number' && Number.isFinite(candidate)) {
          return String(candidate);
        }

        if (typeof candidate === 'string' && candidate.trim().length > 0) {
          return candidate.trim();
        }
      }

      return null;
    }

    function setActiveTab(tabName) {
      tabs.forEach((tab) => tab.classList.toggle('active', tab.dataset.tab === tabName));
      panels.forEach((panel) => panel.classList.toggle('active', panel.dataset.panel === tabName));
      if (tabName === 'switches') {
        renderLinkLines();
      }

      if (tabName === 'monitors') {
        loadAllMonitorItems();
        renderMonitorLinkLines();
      }

      ensureMapAutoRefresh(false);
    }

    tabs.forEach((tab) => {
      tab.addEventListener('click', () => {
        setActiveTab(tab.dataset.tab);
        if (tab.dataset.tab === 'info') {
          loadInfoEvents();
          loadTeamStatus();
        }
      });
    });

    async function loadInfoEvents() {
      try {
        const response = await fetch('/api/info/events');
        const payload = await response.json();

        if (!response.ok || !payload.ok) {
          throw new Error(payload.message || 'Failed to load info data.');
        }

        const byKey = {};
        (payload.items || []).forEach((item) => {
          byKey[item.key] = item;
        });

        bindSmallOilRigRealtime(byKey.smallOilRig);
        bindLargeOilRigRealtime(byKey.largeOilRig);
        infoCargoShip.innerHTML = formatEventValue(byKey.cargoShip);
        infoChinook47.innerHTML = formatEventValue(byKey.chinook47);
        infoPatrolHelicopter.innerHTML = formatEventValue(byKey.patrolHelicopter);
        infoTravelingVendor.innerHTML = formatEventValue(byKey.travelingVendor);

        const refreshedAt = payload.refreshedAtUtc ? new Date(payload.refreshedAtUtc).toLocaleString() : null;
        infoFootnote.textContent = refreshedAt ? `Last updated: ${refreshedAt}` : '';
      } catch (error) {
        infoCargoShip.textContent = 'Unavailable';
        infoChinook47.textContent = 'Unavailable';
        infoPatrolHelicopter.textContent = 'Unavailable';
        infoTravelingVendor.textContent = 'Unavailable';
        infoFootnote.textContent = `Info fetch failed: ${error.message}`;
      }
    }

    async function loadTeamStatus() {
      try {
        const response = await fetch('/api/team/status');
        const payload = await response.json();

        if (!response.ok || !payload.ok) {
          throw new Error(payload.message || 'Failed to load team info.');
        }

        teamList.innerHTML = '';

        (payload.members || []).forEach((member) => {
          const item = document.createElement('li');
          item.className = 'team-row';

          const leaderCrown = member.isLeader
            ? '<span class="team-leader-crown" title="Team Leader">👑</span>'
            : '';
          const presenceSymbol = member.isOnline
            ? '<span class="presence-dot online" title="Online"></span>'
            : '<span class="presence-dot offline" title="Offline"></span>';
          const deadSymbol = member.isAlive
            ? ''
            : '<span class="dead-symbol" title="Dead">💀</span>';

          item.innerHTML = `
            <div class="team-left">
              <span class="team-name">${member.name}${leaderCrown}</span>
            </div>
            <div class="team-badges">
              ${deadSymbol}
              ${presenceSymbol}
            </div>`;

          teamList.appendChild(item);
        });

        if ((payload.members || []).length === 0) {
          teamList.innerHTML = '<li class="team-row"><span class="team-name">No team members found.</span></li>';
        }

        const refreshedAt = payload.refreshedAtUtc ? new Date(payload.refreshedAtUtc).toLocaleString() : null;
        teamFootnote.textContent = refreshedAt ? `Team updated: ${refreshedAt}` : '';
      } catch (error) {
        teamList.innerHTML = '<li class="team-row"><span class="team-name">Unable to load team status.</span></li>';
        teamFootnote.textContent = `Team fetch failed: ${error.message}`;
      }
    }

    function updateMapLegend(items) {
      if (!mapLegend) {
        return;
      }

      mapLegend.innerHTML = '';

      const legendEntries = [
        { label: 'Monuments', key: 'monument' },
        { label: 'Vending Machines', key: 'vendingMachine' },
        { label: 'Cargo Ship', key: 'cargoShip' },
        { label: 'Chinook', key: 'ch47' },
        { label: 'Patrol Helicopter', key: 'patrolHelicopter' },
        { label: 'Travelling Vendor', key: 'travelingVendor' }
      ];

      legendEntries.forEach((entry) => {
        const count = items[entry.key] || 0;
        const item = document.createElement('li');
        item.className = 'info-row';
        item.innerHTML = `<span>${entry.label}</span><span>${count}</span>`;
        mapLegend.appendChild(item);
      });
    }

    function getMapCoordinateBounds(monuments, markers) {
      let minX = Number.POSITIVE_INFINITY;
      let maxX = Number.NEGATIVE_INFINITY;
      let minY = Number.POSITIVE_INFINITY;
      let maxY = Number.NEGATIVE_INFINITY;

      const include = (x, y) => {
        if (typeof x !== 'number' || typeof y !== 'number') {
          return;
        }

        minX = Math.min(minX, x);
        maxX = Math.max(maxX, x);
        minY = Math.min(minY, y);
        maxY = Math.max(maxY, y);
      };

      monuments.forEach((monument) => include(monument?.x, monument?.y));
      markers.forEach((marker) => include(marker?.x, marker?.y));

      if (!Number.isFinite(minX) || !Number.isFinite(maxX) || !Number.isFinite(minY) || !Number.isFinite(maxY)) {
        return null;
      }

      return { minX, maxX, minY, maxY };
    }

    function projectMapToCanvasX(mapX, projection) {
      const normalized = (mapX + projection.oceanMargin) / projection.worldWidth;
      return normalized * mapCanvas.width;
    }

    function projectMapToCanvasY(mapY, projection) {
      const normalized = (mapY + projection.oceanMargin) / projection.worldHeight;
      return mapCanvas.height - (normalized * mapCanvas.height);
    }

    function projectCanvasToMapX(canvasX, projection) {
      const normalized = canvasX / mapCanvas.width;
      return (normalized * projection.worldWidth) - projection.oceanMargin;
    }

    function projectCanvasToMapY(canvasY, projection) {
      const normalized = 1 - (canvasY / mapCanvas.height);
      return (normalized * projection.worldHeight) - projection.oceanMargin;
    }

    function updateMapDebugOverlay() {
      if (!mapDebugPanel) {
        return;
      }

      if (!mapDebugToggle?.checked || !mapRenderContext) {
        mapDebugPanel.textContent = '';
        return;
      }

      const lines = [];
      lines.push(`Map size: ${mapRenderContext.mapWidth} x ${mapRenderContext.mapHeight}`);
      lines.push(`Ocean margin: ${mapRenderContext.oceanMargin}`);
      lines.push(`World size: ${mapRenderContext.worldWidth} x ${mapRenderContext.worldHeight}`);
      lines.push(`Scale: x=${mapRenderContext.scaleX.toFixed(4)} y=${mapRenderContext.scaleY.toFixed(4)}`);

      if (mapRenderContext.bounds) {
        lines.push(
          `Bounds: x=[${mapRenderContext.bounds.minX.toFixed(2)} .. ${mapRenderContext.bounds.maxX.toFixed(2)}], y=[${mapRenderContext.bounds.minY.toFixed(2)} .. ${mapRenderContext.bounds.maxY.toFixed(2)}]`
        );
      }

      if (mapDebugLastClick) {
        lines.push(`Click canvas: (${mapDebugLastClick.canvasX.toFixed(1)}, ${mapDebugLastClick.canvasY.toFixed(1)})`);
        lines.push(`Click raw map: (${mapDebugLastClick.rawX.toFixed(2)}, ${mapDebugLastClick.rawY.toFixed(2)})`);
        lines.push(`Click margin-adjusted: (${mapDebugLastClick.marginAdjustedX.toFixed(2)}, ${mapDebugLastClick.marginAdjustedY.toFixed(2)})`);

        if (mapDebugLastClick.nearestVending) {
          lines.push(
            `Nearest vending: #${mapDebugLastClick.nearestVending.id} @ (${mapDebugLastClick.nearestVending.x.toFixed(2)}, ${mapDebugLastClick.nearestVending.y.toFixed(2)}), distance=${mapDebugLastClick.nearestVending.distance.toFixed(2)}`
          );
        } else {
          lines.push('Nearest vending: none');
        }
      } else {
        lines.push('Click on the map to inspect coordinates and nearest vending marker.');
      }

      mapDebugPanel.textContent = lines.join('\n');
    }

    function ensureMapAutoRefresh(isMapTabActive) {
      if (mapAutoRefreshTimer) {
        clearInterval(mapAutoRefreshTimer);
        mapAutoRefreshTimer = null;
      }

      if (!isMapTabActive) {
        return;
      }

      mapAutoRefreshTimer = setInterval(() => {
        loadMapData();
      }, mapRefreshIntervalMs);
    }

    function getOrLoadMapImage(imageDataUrl) {
      if (!imageDataUrl) {
        return Promise.resolve(null);
      }

      const cachedPromise = mapImagePromiseCache.get(imageDataUrl);
      if (cachedPromise) {
        return cachedPromise;
      }

      const imagePromise = new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => {
          mapImagePromiseCache.delete(imageDataUrl);
          reject(new Error('Failed to load map image.'));
        };
        image.src = imageDataUrl;
      });

          }
          if (mySystemMonitorStatus) {
            mySystemMonitorStatus.textContent = `Items: ${items.length}`;
      mapImagePromiseCache.set(imageDataUrl, imagePromise);
      return imagePromise;
    }

    async function renderMap(payload) {
      if (!mapCanvas) {
        return;
      }

      const context = mapCanvas.getContext('2d');
      if (!context) {
        return;
      }

      const mapWidth = Number(payload?.map?.width) || 4500;
      const mapHeight = Number(payload?.map?.height) || 4500;
      const oceanMargin = Number(payload?.map?.oceanMargin) || 0;
      const mapImageDataUrl = typeof payload?.map?.imageDataUrl === 'string' ? payload.map.imageDataUrl : '';
      const monuments = Array.isArray(payload?.map?.monuments) ? payload.map.monuments : [];
      const markers = Array.isArray(payload?.markers) ? payload.markers : [];

      if (mapImageDataUrl) {
        try {
          const image = await getOrLoadMapImage(mapImageDataUrl);
          if (image) {
            context.drawImage(image, 0, 0, mapCanvas.width, mapCanvas.height);
          }
        } catch {
          context.fillStyle = '#2f2f2f';
          context.fillRect(0, 0, mapCanvas.width, mapCanvas.height);
        }
      } else {
        context.fillStyle = '#2f2f2f';
        context.fillRect(0, 0, mapCanvas.width, mapCanvas.height);
      }

      context.strokeStyle = '#555555';
      context.lineWidth = 1;
      context.strokeRect(0, 0, mapCanvas.width, mapCanvas.height);

      const worldWidth = mapWidth + (oceanMargin * 2);
      const worldHeight = mapHeight + (oceanMargin * 2);
      const safeWorldWidth = worldWidth > 0 ? worldWidth : mapWidth;
      const safeWorldHeight = worldHeight > 0 ? worldHeight : mapHeight;
      const scaleX = mapCanvas.width / safeWorldWidth;
      const scaleY = mapCanvas.height / safeWorldHeight;
      const canvasCenterX = mapCanvas.width / 2;
      const canvasCenterY = mapCanvas.height / 2;
      const coordinateBounds = getMapCoordinateBounds(monuments, markers);
      mapRenderContext = {
        mapWidth,
        mapHeight,
        oceanMargin,
        worldWidth: safeWorldWidth,
        worldHeight: safeWorldHeight,
        scaleX,
        scaleY,
        bounds: coordinateBounds,
        markers
      };

      const markerCounts = {
        monument: 0,
        vendingMachine: 0,
        cargoShip: 0,
        ch47: 0,
        patrolHelicopter: 0,
        travelingVendor: 0
      };

      monuments.forEach((monument) => {
        if (typeof monument?.x !== 'number' || typeof monument?.y !== 'number') {
          return;
        }

        const x = projectMapToCanvasX(monument.x, mapRenderContext);
        const y = projectMapToCanvasY(monument.y, mapRenderContext);
        markerCounts.monument += 1;

        context.fillStyle = '#e6e6e6';
        context.beginPath();
        context.arc(x, y, 2, 0, Math.PI * 2);
        context.fill();
      });

      markers.forEach((marker) => {
        if (typeof marker?.x !== 'number' || typeof marker?.y !== 'number') {
          return;
        }

        const x = projectMapToCanvasX(marker.x, mapRenderContext);
        const y = projectMapToCanvasY(marker.y, mapRenderContext);
        const markerType = marker.type;
        let renderX = x;
        let renderY = y;

        if (markerType === 'cargoShip') {
          context.fillStyle = '#39ff78';
          markerCounts.cargoShip += 1;
        } else if (markerType === 'ch47') {
          context.fillStyle = '#f06f3c';
          markerCounts.ch47 += 1;
        } else if (markerType === 'patrolHelicopter') {
          context.fillStyle = '#ffb08f';
          markerCounts.patrolHelicopter += 1;
        } else if (markerType === 'travelingVendor') {
          context.fillStyle = '#b9ffd0';
          markerCounts.travelingVendor += 1;
        } else if (markerType === 'vendingMachine') {
          context.fillStyle = '#45d7ff';
          markerCounts.vendingMachine += 1;
          renderX = canvasCenterX + ((x - canvasCenterX) * vendingOverlayScale) + vendingOverlayOffsetX;
          renderY = canvasCenterY + ((y - canvasCenterY) * vendingOverlayScale) + vendingOverlayOffsetY;
        } else {
          context.fillStyle = '#cfcfcf';
        }

        context.beginPath();
        context.arc(renderX, renderY, 4, 0, Math.PI * 2);
        context.fill();
      });

      if (mapDebugToggle?.checked && mapDebugLastClick) {
        context.strokeStyle = '#ffffff';
        context.lineWidth = 1;
        context.beginPath();
        context.moveTo(mapDebugLastClick.canvasX - 8, mapDebugLastClick.canvasY);
        context.lineTo(mapDebugLastClick.canvasX + 8, mapDebugLastClick.canvasY);
        context.moveTo(mapDebugLastClick.canvasX, mapDebugLastClick.canvasY - 8);
        context.lineTo(mapDebugLastClick.canvasX, mapDebugLastClick.canvasY + 8);
        context.stroke();
      }

      updateMapLegend(markerCounts);
      updateMapDebugOverlay();
    }

    async function loadMapData(options = {}) {
      if (!mapStatus) {
        return;
      }

      const force = Boolean(options.force);
      const now = Date.now();
      const elapsedSinceLastRequest = now - mapLastRequestedAtMs;

      if (!force && mapLastRequestedAtMs > 0 && elapsedSinceLastRequest < mapRefreshIntervalMs) {
        const secondsRemaining = Math.ceil((mapRefreshIntervalMs - elapsedSinceLastRequest) / 1000);
        mapStatus.textContent = `Map refresh limited to once per minute. Next update in ${secondsRemaining}s.`;
        return;
      }

      if (mapRequestInFlight) {
        return mapRequestInFlight;
      }

      mapStatus.textContent = 'Loading map...';

      mapRequestInFlight = (async () => {
        mapLastRequestedAtMs = Date.now();

        try {
          const response = await fetch('/api/map');
          const payload = await response.json();

          if (!response.ok || !payload.ok) {
            throw new Error(payload.message || 'Failed to load map.');
          }

          latestMapPayload = payload;
          await renderMap(payload);
          mapStatus.textContent = 'Map loaded.';

          const refreshedAt = payload.refreshedAtUtc ? new Date(payload.refreshedAtUtc).toLocaleString() : null;
          if (mapFootnote) {
            mapFootnote.textContent = refreshedAt ? `Map updated: ${refreshedAt}` : '';
          }
        } catch (error) {
          mapStatus.textContent = `Map fetch failed: ${error.message}`;
          if (mapFootnote) {
            mapFootnote.textContent = '';
          }

          if (latestMapPayload) {
            await renderMap(latestMapPayload);
          }
        } finally {
          mapRequestInFlight = null;
        }
      })();

      return mapRequestInFlight;
    }

    function bindSmallOilRigRealtime(item) {
      if (smallOilRigTicker) {
        clearInterval(smallOilRigTicker);
        smallOilRigTicker = null;
      }

      if (!item) {
        infoSmallOilRig.textContent = 'No recent activity seen';
        return;
      }

      const countdownEndsAt = item.countdownEndsAtUtc ? new Date(item.countdownEndsAtUtc) : null;
      const lastActiveUtc = item.lastActiveUtc ? new Date(item.lastActiveUtc) : null;

      const render = () => {
        const now = new Date();

        if (countdownEndsAt && countdownEndsAt > now) {
          const remainingMs = countdownEndsAt.getTime() - now.getTime();
          const totalSeconds = Math.max(0, Math.floor(remainingMs / 1000));
          const minutes = Math.floor(totalSeconds / 60);
          const seconds = totalSeconds % 60;
          infoSmallOilRig.textContent = `${minutes}m ${String(seconds).padStart(2, '0')}s for crate`;
          return;
        }

        if (item.status === 'Active now') {
          infoSmallOilRig.innerHTML = activeIndicator();
          return;
        }

        if (!lastActiveUtc) {
          infoSmallOilRig.textContent = 'No recent activity seen';
          return;
        }

        const elapsedMs = Math.max(0, now.getTime() - lastActiveUtc.getTime());
        const elapsedSeconds = Math.floor(elapsedMs / 1000);
        const days = Math.floor(elapsedSeconds / 86400);
        const hours = Math.floor((elapsedSeconds % 86400) / 3600);
        const minutes = Math.floor((elapsedSeconds % 3600) / 60);
        const seconds = elapsedSeconds % 60;

        if (days > 0) {
          infoSmallOilRig.textContent = `${days}d ${hours}h ${minutes}m since last event`;
          return;
        }

        if (hours > 0) {
          infoSmallOilRig.textContent = `${hours}h ${minutes}m ${seconds}s since last event`;
          return;
        }

        infoSmallOilRig.textContent = `${minutes}m ${seconds}s since last event`;
      };

      render();
      smallOilRigTicker = setInterval(render, 1000);
    }

    function bindLargeOilRigRealtime(item) {
      if (largeOilRigTicker) {
        clearInterval(largeOilRigTicker);
        largeOilRigTicker = null;
      }

      if (!item) {
        infoLargeOilRig.textContent = 'No recent activity seen';
        return;
      }

      const countdownEndsAt = item.countdownEndsAtUtc ? new Date(item.countdownEndsAtUtc) : null;
      const lastActiveUtc = item.lastActiveUtc ? new Date(item.lastActiveUtc) : null;

      const render = () => {
        const now = new Date();

        if (countdownEndsAt && countdownEndsAt > now) {
          const remainingMs = countdownEndsAt.getTime() - now.getTime();
          const totalSeconds = Math.max(0, Math.floor(remainingMs / 1000));
          const minutes = Math.floor(totalSeconds / 60);
          const seconds = totalSeconds % 60;
          infoLargeOilRig.textContent = `${minutes}m ${String(seconds).padStart(2, '0')}s for crate`;
          return;
        }

        if (item.status === 'Active now') {
          infoLargeOilRig.innerHTML = activeIndicator();
          return;
        }

        if (!lastActiveUtc) {
          infoLargeOilRig.textContent = 'No recent activity seen';
          return;
        }

        const elapsedMs = Math.max(0, now.getTime() - lastActiveUtc.getTime());
        const elapsedSeconds = Math.floor(elapsedMs / 1000);
        const days = Math.floor(elapsedSeconds / 86400);
        const hours = Math.floor((elapsedSeconds % 86400) / 3600);
        const minutes = Math.floor((elapsedSeconds % 3600) / 60);
        const seconds = elapsedSeconds % 60;

        if (days > 0) {
          infoLargeOilRig.textContent = `${days}d ${hours}h ${minutes}m since last event`;
          return;
        }

        if (hours > 0) {
          infoLargeOilRig.textContent = `${hours}h ${minutes}m ${seconds}s since last event`;
          return;
        }

        infoLargeOilRig.textContent = `${minutes}m ${seconds}s since last event`;
      };

      render();
      largeOilRigTicker = setInterval(render, 1000);
    }

    function getSavedSwitches() {
      try {
        const raw = readStoredValue(switchStorageKey);
        if (!raw) {
          return [];
        }

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    function saveSwitches(items) {
      writeStoredValue(switchStorageKey, JSON.stringify(items));
    }

    function getSavedMonitors() {
      try {
        const raw = readStoredValue(monitorStorageKey);
        if (!raw) {
          return [];
        }

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    function saveMonitors(items) {
      writeStoredValue(monitorStorageKey, JSON.stringify(items));
    }

    function getSavedVirtualMonitors() {
      try {
        const raw = readStoredValue(virtualMonitorStorageKey);
        if (!raw) {
          return [];
        }

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    function saveVirtualMonitors(items) {
      writeStoredValue(virtualMonitorStorageKey, JSON.stringify(items));
    }

    function getVirtualMonitorLinks() {
      try {
        const raw = readStoredValue(virtualMonitorLinksStorageKey);
        if (!raw) {
          return {};
        }

        const parsed = JSON.parse(raw);
        return parsed && typeof parsed === 'object' ? parsed : {};
      } catch {
        return {};
      }
    }

    function saveVirtualMonitorLinks(links) {
      writeStoredValue(virtualMonitorLinksStorageKey, JSON.stringify(links));
    }

    function getLinkedMonitorIds(virtualMonitorId) {
      const links = getVirtualMonitorLinks();
      const ids = links[virtualMonitorId];
      return Array.isArray(ids) ? ids : [];
    }

    function addVirtualMonitorLink(virtualMonitorId, monitorId) {
      const links = getVirtualMonitorLinks();
      const current = Array.isArray(links[virtualMonitorId]) ? links[virtualMonitorId] : [];
      if (current.includes(monitorId)) {
        return false;
      }

      links[virtualMonitorId] = [...current, monitorId];
      saveVirtualMonitorLinks(links);
      return true;
    }

    function removeVirtualMonitorLink(virtualMonitorId, monitorId) {
      const links = getVirtualMonitorLinks();
      const current = Array.isArray(links[virtualMonitorId]) ? links[virtualMonitorId] : [];
      if (!current.includes(monitorId)) {
        return false;
      }

      const updated = current.filter((id) => id !== monitorId);
      if (updated.length === 0) {
        delete links[virtualMonitorId];
      } else {
        links[virtualMonitorId] = updated;
      }

      saveVirtualMonitorLinks(links);
      return true;
    }

    function removeVirtualMonitorLinksByVirtualId(virtualMonitorId) {
      const links = getVirtualMonitorLinks();
      if (!(virtualMonitorId in links)) {
        return;
      }

      delete links[virtualMonitorId];
      saveVirtualMonitorLinks(links);
    }

    function removeVirtualMonitorLinksByRealId(monitorId) {
      const links = getVirtualMonitorLinks();
      const updated = {};

      Object.entries(links).forEach(([virtualMonitorId, monitorIds]) => {
        const filtered = Array.isArray(monitorIds) ? monitorIds.filter((id) => id !== monitorId) : [];
        if (filtered.length > 0) {
          updated[virtualMonitorId] = filtered;
        }
      });

      saveVirtualMonitorLinks(updated);
    }

    function getLinkedVirtualMonitorIds(realMonitorId) {
      const links = getVirtualMonitorLinks();
      return Object.entries(links)
        .filter(([, monitorIds]) => Array.isArray(monitorIds) && monitorIds.includes(realMonitorId))
        .map(([virtualMonitorId]) => virtualMonitorId);
    }

    function getSavedVirtualSwitches() {
      try {
        const raw = readStoredValue(virtualSwitchStorageKey);
        if (!raw) {
          return [];
        }

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    function saveVirtualSwitches(items) {
      writeStoredValue(virtualSwitchStorageKey, JSON.stringify(items));
    }

    function getVirtualSwitchLinks() {
      try {
        const raw = readStoredValue(virtualSwitchLinksStorageKey);
        if (!raw) {
          return {};
        }

        const parsed = JSON.parse(raw);
        return parsed && typeof parsed === 'object' ? parsed : {};
      } catch {
        return {};
      }
    }

    function saveVirtualSwitchLinks(links) {
      writeStoredValue(virtualSwitchLinksStorageKey, JSON.stringify(links));
    }

    function getVirtualSwitchLinkJoints() {
      try {
        const raw = readStoredValue(virtualSwitchLinkJointsStorageKey);
        if (!raw) {
          return {};
        }

        const parsed = JSON.parse(raw);
        return parsed && typeof parsed === 'object' ? parsed : {};
      } catch {
        return {};
      }
    }

    function saveVirtualSwitchLinkJoints(joints) {
      writeStoredValue(virtualSwitchLinkJointsStorageKey, JSON.stringify(joints));
    }

    function buildLinkKey(virtualId, realSwitchId) {
      return `${virtualId}::${realSwitchId}`;
    }

    function parseLinkKey(linkKey) {
      const [virtualId, realSwitchId] = String(linkKey || '').split('::');
      if (!virtualId || !realSwitchId) {
        return null;
      }

      return { virtualId, realSwitchId };
    }

    function getJointsForLink(virtualId, realSwitchId) {
      const jointsMap = getVirtualSwitchLinkJoints();
      const key = buildLinkKey(virtualId, realSwitchId);
      const joints = jointsMap[key];
      return Array.isArray(joints) ? joints : [];
    }

    function saveJointsForLink(virtualId, realSwitchId, joints) {
      const jointsMap = getVirtualSwitchLinkJoints();
      const key = buildLinkKey(virtualId, realSwitchId);
      jointsMap[key] = joints;
      saveVirtualSwitchLinkJoints(jointsMap);
    }

    function removeJointsForLink(virtualId, realSwitchId) {
      const jointsMap = getVirtualSwitchLinkJoints();
      const key = buildLinkKey(virtualId, realSwitchId);
      if (!(key in jointsMap)) {
        return;
      }

      delete jointsMap[key];
      saveVirtualSwitchLinkJoints(jointsMap);
    }

    function getLinkedRealSwitchIds(virtualId) {
      const links = getVirtualSwitchLinks();
      const ids = links[virtualId];
      return Array.isArray(ids) ? ids : [];
    }

    function addVirtualSwitchLink(virtualId, realSwitchId) {
      const links = getVirtualSwitchLinks();
      const current = Array.isArray(links[virtualId]) ? links[virtualId] : [];
      if (current.includes(realSwitchId)) {
        return false;
      }

      links[virtualId] = [...current, realSwitchId];
      saveVirtualSwitchLinks(links);
      return true;
    }

    function removeVirtualSwitchLinksByVirtualId(virtualId) {
      const links = getVirtualSwitchLinks();
      if (!(virtualId in links)) {
        return;
      }

      const realIds = Array.isArray(links[virtualId]) ? links[virtualId] : [];
      realIds.forEach((realId) => {
        removeJointsForLink(virtualId, realId);
      });

      delete links[virtualId];
      saveVirtualSwitchLinks(links);
    }

    function removeVirtualSwitchLinksByRealId(realSwitchId) {
      const links = getVirtualSwitchLinks();
      const updated = {};

      Object.entries(links).forEach(([virtualId, realIds]) => {
        const list = Array.isArray(realIds) ? realIds.filter((id) => id !== realSwitchId) : [];
        if (list.length > 0) {
          updated[virtualId] = list;
        }

        if (Array.isArray(realIds) && realIds.includes(realSwitchId)) {
          removeJointsForLink(virtualId, realSwitchId);
        }
      });

      saveVirtualSwitchLinks(updated);
    }

    function removeVirtualToRealLink(virtualId, realSwitchId) {
      const links = getVirtualSwitchLinks();
      const current = Array.isArray(links[virtualId]) ? links[virtualId] : [];
      if (!current.includes(realSwitchId)) {
        return false;
      }

      const updated = current.filter((id) => id !== realSwitchId);
      if (updated.length === 0) {
        delete links[virtualId];
      } else {
        links[virtualId] = updated;
      }

      removeJointsForLink(virtualId, realSwitchId);

      saveVirtualSwitchLinks(links);
      return true;
    }

    function getLinkedVirtualIdsForReal(realSwitchId) {
      const links = getVirtualSwitchLinks();
      return Object.entries(links)
        .filter(([, realIds]) => Array.isArray(realIds) && realIds.includes(realSwitchId))
        .map(([virtualId]) => virtualId);
    }

    function getElementCenterInArea(element, areaElement) {
      if (!element || !areaElement) {
        return null;
      }

      const areaRect = areaElement.getBoundingClientRect();
      const rect = element.getBoundingClientRect();
      return {
        x: rect.left - areaRect.left + (rect.width / 2),
        y: rect.top - areaRect.top + (rect.height / 2)
      };
    }

    function getElementCenterInWiringArea(element) {
      return getElementCenterInArea(element, switchWiringArea);
    }

    function getElementCenterInMonitorArea(element) {
      return getElementCenterInArea(element, monitorWiringArea);
    }

    function scheduleRenderLinkLines() {
      if (switchLinkRenderScheduled) {
        return;
      }

      switchLinkRenderScheduled = true;
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          switchLinkRenderScheduled = false;
          renderLinkLines();
        });
      });
    }

    function scheduleRenderMonitorLinkLines() {
      if (monitorLinkRenderScheduled) {
        return;
      }

      monitorLinkRenderScheduled = true;
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          monitorLinkRenderScheduled = false;
          renderMonitorLinkLines();
        });
      });
    }

    function createPreviewPoints(sourcePoint, currentPoint) {
      const points = [sourcePoint];

      if (currentPoint && Number.isFinite(currentPoint.x) && Number.isFinite(currentPoint.y)) {
        points.push(currentPoint);
      }

      return points;
    }

    function ensureSvgSizeMatchesArea(areaElement, svgElement) {
      if (!areaElement || !svgElement) {
        return;
      }

      const rect = areaElement.getBoundingClientRect();
      svgElement.setAttribute('width', String(rect.width));
      svgElement.setAttribute('height', String(rect.height));
      svgElement.setAttribute('viewBox', `0 0 ${rect.width} ${rect.height}`);
    }

    function createLinkLine(x1, y1, x2, y2, cssClass) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', String(x1));
      line.setAttribute('y1', String(y1));
      line.setAttribute('x2', String(x2));
      line.setAttribute('y2', String(y2));
      line.setAttribute('class', cssClass);
      return line;
    }

    function createLinkPath(points, cssClass) {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
      path.setAttribute('points', points.map((point) => `${point.x},${point.y}`).join(' '));
      path.setAttribute('fill', 'none');
      path.setAttribute('class', cssClass);
      return path;
    }

    function renderLinkLines() {
      if (!switchLinkSvg) {
        return;
      }

      ensureSvgSizeMatchesArea(switchWiringArea, switchLinkSvg);
      switchLinkSvg.innerHTML = '';

      const linkPathLayer = document.createElementNS('http://www.w3.org/2000/svg', 'g');

      const links = getVirtualSwitchLinks();
      Object.entries(links).forEach(([virtualId, realIds]) => {
        const source = document.querySelector(`.virtual-power-out[data-virtual-id="${virtualId}"]`);
        const sourcePoint = getElementCenterInWiringArea(source);
        if (!sourcePoint || !Array.isArray(realIds)) {
          return;
        }

        realIds.forEach((realId) => {
          const target = document.querySelector(`.real-power-in[data-real-id="${realId}"]`);
          const targetPoint = getElementCenterInWiringArea(target);
          if (!targetPoint) {
            return;
          }
          linkPathLayer.appendChild(createLinkPath([sourcePoint, targetPoint], 'switch-link-line'));
        });
      });

      if (activeWiringVirtualId) {
        const source = document.querySelector(`.virtual-power-out[data-virtual-id="${activeWiringVirtualId}"]`);
        const sourcePoint = getElementCenterInWiringArea(source);
        const previewPoints = createPreviewPoints(sourcePoint, previewCursorPoint);
        if (previewPoints.length >= 2) {
          linkPathLayer.appendChild(createLinkPath(previewPoints, 'switch-link-preview'));
        }
      }

      switchLinkSvg.appendChild(linkPathLayer);
    }

    function renderMonitorLinkLines() {
      if (!monitorLinkSvg) {
        return;
      }

      ensureSvgSizeMatchesArea(monitorWiringArea, monitorLinkSvg);
      monitorLinkSvg.innerHTML = '';

      const linkPathLayer = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      const links = getVirtualMonitorLinks();

      Object.entries(links).forEach(([virtualMonitorId, monitorIds]) => {
        const source = document.querySelector(`.monitor-virtual-out[data-virtual-monitor-id="${virtualMonitorId}"]`);
        const sourcePoint = getElementCenterInMonitorArea(source);
        if (!sourcePoint || !Array.isArray(monitorIds)) {
          return;
        }

        monitorIds.forEach((monitorId) => {
          const target = document.querySelector(`.monitor-real-in[data-monitor-id="${monitorId}"]`);
          const targetPoint = getElementCenterInMonitorArea(target);
          if (!targetPoint) {
            return;
          }

          linkPathLayer.appendChild(createLinkPath([sourcePoint, targetPoint], 'switch-link-line'));
        });
      });

      monitorLinkSvg.appendChild(linkPathLayer);
    }
      function getVirtualMonitorLinkJoints() {
        try {
          const raw = readStoredValue(virtualMonitorLinkJointsStorageKey);
          if (!raw) {
            return {};
          }
          const parsed = JSON.parse(raw);
          return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
          return {};
        }
      }

      function saveVirtualMonitorLinkJoints(joints) {
        writeStoredValue(virtualMonitorLinkJointsStorageKey, JSON.stringify(joints));
      }

      function buildMonitorLinkKey(virtualMonitorId, monitorId) {
        return `${virtualMonitorId}::${monitorId}`;
      }

      function getMonitorJointsForLink(virtualMonitorId, monitorId) {
        const jointsMap = getVirtualMonitorLinkJoints();
        const key = buildMonitorLinkKey(virtualMonitorId, monitorId);
        const joints = jointsMap[key];
        return Array.isArray(joints) ? joints : [];
      }

      function saveMonitorJointsForLink(virtualMonitorId, monitorId, joints) {
        const jointsMap = getVirtualMonitorLinkJoints();
        const key = buildMonitorLinkKey(virtualMonitorId, monitorId);
        jointsMap[key] = joints;
        saveVirtualMonitorLinkJoints(jointsMap);
      }

      function removeMonitorJointsForLink(virtualMonitorId, monitorId) {
        const jointsMap = getVirtualMonitorLinkJoints();
        const key = buildMonitorLinkKey(virtualMonitorId, monitorId);
        if (!(key in jointsMap)) {
          return;
        }
        delete jointsMap[key];
        saveVirtualMonitorLinkJoints(jointsMap);
      }

      function renderMonitorLinkLines() {
        if (!monitorLinkSvg) {
          return;
        }

        ensureSvgSizeMatchesArea(monitorWiringArea, monitorLinkSvg);
        monitorLinkSvg.innerHTML = '';

        const linkPathLayer = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        const links = getVirtualMonitorLinks();

        Object.entries(links).forEach(([virtualMonitorId, monitorIds]) => {
          const source = document.querySelector(`.monitor-virtual-out[data-virtual-monitor-id="${virtualMonitorId}"]`);
          const sourcePoint = getElementCenterInMonitorArea(source);
          if (!sourcePoint || !Array.isArray(monitorIds)) {
            return;
          }

          monitorIds.forEach((monitorId) => {
            const target = document.querySelector(`.monitor-real-in[data-monitor-id="${monitorId}"]`);
            const targetPoint = getElementCenterInMonitorArea(target);
            if (!targetPoint) {
              return;
            }
            linkPathLayer.appendChild(createLinkPath([sourcePoint, targetPoint], 'switch-link-line'));
          });
        });

        if (activeVirtualMonitorWiringId) {
          const source = document.querySelector(`.monitor-virtual-out[data-virtual-monitor-id="${activeVirtualMonitorWiringId}"]`);
          const sourcePoint = getElementCenterInMonitorArea(source);
          const previewPoints = createPreviewPoints(sourcePoint, previewMonitorCursorPoint);
          if (previewPoints.length >= 2) {
            linkPathLayer.appendChild(createLinkPath(previewPoints, 'switch-link-preview'));
          }
        }

        monitorLinkSvg.appendChild(linkPathLayer);
      }

    function startWiringFromVirtual(virtualId) {
      activeWiringVirtualId = virtualId;
      previewCursorPoint = null;
      draftWiringJoints = [];

      document.querySelectorAll('.virtual-power-out.wiring-active').forEach((entry) => {
        entry.classList.remove('wiring-active');
      });

      const current = document.querySelector(`.virtual-power-out[data-virtual-id="${virtualId}"]`);
      if (current) {
        current.classList.add('wiring-active');
      }
      scheduleRenderLinkLines();
    }

    function clearWiringMode() {
      activeWiringVirtualId = null;
      previewCursorPoint = null;
      draftWiringJoints = [];
      document.querySelectorAll('.virtual-power-out.wiring-active').forEach((entry) => {
        entry.classList.remove('wiring-active');
      });
      scheduleRenderLinkLines();
    }

    function startMonitorWiringFromVirtual(virtualMonitorId) {
      activeVirtualMonitorWiringId = virtualMonitorId;

      document.querySelectorAll('.monitor-virtual-out.wiring-active').forEach((entry) => {
        entry.classList.remove('wiring-active');
      });

      const current = document.querySelector(`.monitor-virtual-out[data-virtual-monitor-id="${virtualMonitorId}"]`);
      if (current) {
        current.classList.add('wiring-active');
      }

      scheduleRenderMonitorLinkLines();
        previewMonitorCursorPoint = null;
        draftMonitorWiringJoints = [];
    }

    function clearMonitorWiringMode() {
      activeVirtualMonitorWiringId = null;
      document.querySelectorAll('.monitor-virtual-out.wiring-active').forEach((entry) => {
        entry.classList.remove('wiring-active');
      });

      scheduleRenderMonitorLinkLines();
        previewMonitorCursorPoint = null;
        draftMonitorWiringJoints = [];
    }

    function clearPendingRemoveButton() {
      if (!pendingRemoveButton) {
        return;
      }

      pendingRemoveButton.textContent = pendingRemoveButton.dataset.defaultLabel || 'Remove';
      pendingRemoveButton.dataset.confirm = 'false';
      pendingRemoveButton = null;
    }

    function setPendingRemoveButton(button) {
      if (!button) {
        return;
      }

      if (pendingRemoveButton && pendingRemoveButton !== button) {
        clearPendingRemoveButton();
      }

      if (!button.dataset.defaultLabel) {
        button.dataset.defaultLabel = button.textContent || 'Remove';
      }

      button.textContent = 'you sure?';
      button.dataset.confirm = 'true';
      pendingRemoveButton = button;
    }

    function renderMonitorItem(monitorEntry) {
      const item = document.createElement('li');
      item.className = 'switch-item monitor-item is-collapsed';
      item.dataset.monitorId = monitorEntry.id;
      item.innerHTML = `
        <button type="button" class="monitor-real-in" data-monitor-id="${monitorEntry.id}" title="Connect virtual box here"></button>
        <div class="monitor-header">
          <div class="switch-meta">
            <span class="switch-name">${monitorEntry.name}</span>
            <span class="switch-id">ID: ${monitorEntry.id}</span>
            <span class="switch-id monitor-count" data-id="${monitorEntry.id}">Items: 0</span>
          </div>
          <div class="switch-controls">
            <span class="switch-state monitor-state monitor-hide-when-collapsed" data-state="unknown">Loading...</span>
            <button type="button" class="switch-action monitor-refresh monitor-hide-when-collapsed" data-id="${monitorEntry.id}">Refresh</button>
            <button type="button" class="switch-action monitor-break-link monitor-hide-when-collapsed" data-id="${monitorEntry.id}">Break Link</button>
            <button type="button" class="switch-action monitor-remove monitor-hide-when-collapsed" data-id="${monitorEntry.id}">Remove Monitor</button>
            <button type="button" class="switch-action monitor-toggle" data-id="${monitorEntry.id}">Open</button>
          </div>
        </div>
        <div class="monitor-crate" aria-label="Storage monitor inventory">
          <div class="monitor-crate-placeholder" aria-hidden="true"></div>
          <div class="monitor-crate-grid" aria-hidden="true"></div>
        </div>
        `;

      monitorList.appendChild(item);
    }

    function renderVirtualMonitorItem(virtualMonitorEntry) {
      const linkedCount = getLinkedMonitorIds(virtualMonitorEntry.id).length;
      const item = document.createElement('li');
      item.className = 'switch-item monitor-item virtual-monitor-item';
      item.dataset.virtualMonitorId = virtualMonitorEntry.id;
      item.innerHTML = `
        <button type="button" class="monitor-virtual-out" data-virtual-monitor-id="${virtualMonitorEntry.id}" title="Click then click a real storage monitor to connect"></button>
        <div class="monitor-header">
          <div class="switch-meta">
            <span class="switch-name">${virtualMonitorEntry.name}</span>
            <span class="switch-id">Linked Boxes: <span class="virtual-monitor-linked-count" data-id="${virtualMonitorEntry.id}">${linkedCount}</span></span>
            <span class="switch-id virtual-monitor-count" data-id="${virtualMonitorEntry.id}">Items: 0</span>
          </div>
          <div class="switch-controls">
            <button type="button" class="switch-action virtual-monitor-break-all" data-id="${virtualMonitorEntry.id}">Break All Links</button>
            <button type="button" class="switch-action virtual-monitor-remove" data-id="${virtualMonitorEntry.id}">Remove Virtual Box</button>
          </div>
        </div>
        <div class="monitor-virtual-grid" data-id="${virtualMonitorEntry.id}"></div>
      `;

      virtualMonitorList.appendChild(item);
    }

    function setMonitorItemState(monitorId, stateText, stateValue = 'unknown') {
      const monitorItem = monitorList.querySelector(`.switch-item[data-monitor-id="${monitorId}"]`);
      const stateBadge = monitorItem?.querySelector('.monitor-state');
      if (!stateBadge) {
        return;
      }

      stateBadge.textContent = stateText;
      stateBadge.dataset.state = stateValue;
    }

    function setMonitorItemCount(monitorId, count) {
      const monitorCount = monitorList.querySelector(`.monitor-count[data-id="${monitorId}"]`);
      if (!monitorCount) {
        return;
      }

      monitorCount.textContent = `Items: ${count}`;
    }

    function setVirtualMonitorItemCount(virtualMonitorId, count) {
      const countLabel = virtualMonitorList.querySelector(`.virtual-monitor-count[data-id="${virtualMonitorId}"]`);
      if (!countLabel) {
        return;
      }

      countLabel.textContent = `Items: ${count}`;
    }

    function setVirtualMonitorLinkedCount(virtualMonitorId) {
      const countLabel = virtualMonitorList.querySelector(`.virtual-monitor-linked-count[data-id="${virtualMonitorId}"]`);
      if (!countLabel) {
        return;
      }

      countLabel.textContent = String(getLinkedMonitorIds(virtualMonitorId).length);
    }

    function setMonitorCollapsed(monitorId, isCollapsed) {
      const monitorItem = monitorList.querySelector(`.switch-item[data-monitor-id="${monitorId}"]`);
      if (!monitorItem) {
        return;
      }

      monitorItem.classList.toggle('is-collapsed', isCollapsed);

      const toggleButton = monitorItem.querySelector('.monitor-toggle');
      if (toggleButton) {
        toggleButton.textContent = isCollapsed ? 'Open' : 'Close';
      }

      scheduleRenderMonitorLinkLines();
    }

    function renderItemSlots(itemsContainer, items, slotCount) {
      itemsContainer.innerHTML = '';

      for (let slotIndex = 0; slotIndex < slotCount; slotIndex += 1) {
        const slot = document.createElement('div');
        slot.className = 'monitor-slot';

        const entry = items[slotIndex] || null;
        if (entry) {
          const itemId = document.createElement('span');
          itemId.className = 'monitor-slot-id';
          itemId.textContent = String(entry.itemId ?? '');

          const icon = document.createElement('img');
          icon.className = 'monitor-slot-icon';
          icon.src = resolveMonitorItemIcon(entry);
          icon.alt = String(entry.shortName ?? entry.itemId ?? '');
          icon.addEventListener('load', () => {
            itemId.style.display = 'none';
          });
          icon.addEventListener('error', () => {
            icon.style.display = 'none';
            itemId.style.display = '';
          });

          const quantity = document.createElement('span');
          quantity.className = 'monitor-slot-qty';
          quantity.textContent = String(entry.quantity ?? 0);

          slot.appendChild(icon);
          slot.appendChild(itemId);
          slot.appendChild(quantity);
        }

        itemsContainer.appendChild(slot);
      }
    }

    function renderMonitorItems(monitorId, items) {
      const monitorItem = monitorList.querySelector(`.switch-item[data-monitor-id="${monitorId}"]`);
      const itemsContainer = monitorItem?.querySelector('.monitor-crate-grid');
      if (!itemsContainer) {
        return;
      }

      renderItemSlots(itemsContainer, items, 48);
      setMonitorItemCount(monitorId, items.length);
    }


    function getVirtualMonitorCombinedItems(virtualMonitorId) {
      const linkedMonitorIds = getLinkedMonitorIds(virtualMonitorId);
      const itemMap = new Map();

      linkedMonitorIds.forEach((monitorId) => {
        const monitorItems = monitorItemsById.get(String(monitorId)) || [];
        monitorItems.forEach((item) => {
          const key = String(item.itemId);
          if (itemMap.has(key)) {
            // Sum quantities for duplicate items
            itemMap.get(key).quantity += Number(item.quantity) || 0;
          } else {
            // Clone the item to avoid mutating original
            itemMap.set(key, {
              ...item,
              quantity: Number(item.quantity) || 0
            });
          }
        });
      });

      // Return as array, sorted by itemId for consistency
      return Array.from(itemMap.values()).sort((a, b) => String(a.itemId).localeCompare(String(b.itemId)));
    }


    function renderVirtualMonitorCombinedItems(virtualMonitorId) {
      const virtualGrid = virtualMonitorList.querySelector(`.monitor-virtual-grid[data-id="${virtualMonitorId}"]`);
      if (!virtualGrid) {
        return;
      }

      const combinedItems = getVirtualMonitorCombinedItems(virtualMonitorId);
      // Dynamically determine columns and rows for a square-like grid
      let columns = 6;
      if (combinedItems.length > 0) {
        columns = Math.max(2, Math.ceil(Math.sqrt(combinedItems.length)));
      }
      const rows = Math.max(1, Math.ceil(combinedItems.length / columns));
      const slotCount = rows * columns;

      virtualGrid.style.display = 'grid';
      virtualGrid.style.gridTemplateColumns = `repeat(${columns}, 1fr)`;
      virtualGrid.style.gridTemplateRows = `repeat(${rows}, minmax(40px, auto))`;

      renderItemSlots(virtualGrid, combinedItems, slotCount);
      setVirtualMonitorItemCount(virtualMonitorId, combinedItems.length);
      setVirtualMonitorLinkedCount(virtualMonitorId);
    }

    function renderAllVirtualMonitorCombinedItems() {
      const virtualMonitors = getSavedVirtualMonitors();
      virtualMonitors.forEach((virtualMonitor) => {
        renderVirtualMonitorCombinedItems(virtualMonitor.id);
      });
    }

    function resolveMonitorItemIcon(item) {
      if (item?.iconUrl) {
        return item.iconUrl;
      }

      const key = String(item?.itemId ?? item ?? '');
      return `/resources/items/${encodeURIComponent(key)}.png`;
    }

    async function loadMonitorItems(monitorId, options = {}) {
      const force = Boolean(options.force);
      const now = Date.now();
      const lastRefreshAt = monitorLastRefreshById.get(monitorId) || 0;
      const elapsedSinceLastRefresh = now - lastRefreshAt;

      if (!force && lastRefreshAt > 0 && elapsedSinceLastRefresh < monitorRefreshIntervalMs) {
        const secondsRemaining = Math.ceil((monitorRefreshIntervalMs - elapsedSinceLastRefresh) / 1000);
        throw new Error(`Monitor refresh limited to once per minute. Next update in ${secondsRemaining}s.`);
      }

      monitorLastRefreshById.set(monitorId, Date.now());
      setMonitorItemState(monitorId, 'Loading...', 'unknown');

      const response = await fetch(`/api/monitors/items?id=${encodeURIComponent(monitorId)}`);
      const payload = await response.json();

      if (!response.ok || !payload.ok) {
        throw new Error(payload.message || 'Failed to load storage monitor items.');
      }

      const items = Array.isArray(payload?.items) ? payload.items : [];
      monitorItemsById.set(String(monitorId), items);
      renderMonitorItems(monitorId, items);
      getLinkedVirtualMonitorIds(String(monitorId)).forEach((virtualMonitorId) => {
        renderVirtualMonitorCombinedItems(virtualMonitorId);
      });
      setMonitorItemState(monitorId, `Items: ${(payload.items || []).length}`, 'on');
    }

    async function loadAllMonitorItems(options = {}) {
      const force = Boolean(options.force);
      const now = Date.now();
      const elapsedSinceLastBulkRefresh = now - monitorLastBulkRefreshAtMs;

      if (!force && monitorLastBulkRefreshAtMs > 0 && elapsedSinceLastBulkRefresh < monitorRefreshIntervalMs) {
        const secondsRemaining = Math.ceil((monitorRefreshIntervalMs - elapsedSinceLastBulkRefresh) / 1000);
        if (monitorActionStatus) {
          monitorActionStatus.textContent = `Monitor refresh limited to once per minute. Next update in ${secondsRemaining}s.`;
        }
        return;
      }

      if (monitorRefreshInFlight) {
        return monitorRefreshInFlight;
      }

      const monitors = getSavedMonitors();
      monitorRefreshInFlight = (async () => {
        monitorLastBulkRefreshAtMs = Date.now();

        for (let index = 0; index < monitors.length; index += 1) {
          const monitor = monitors[index];

          try {
            await loadMonitorItems(monitor.id, { force: true });
          } catch {
            setMonitorItemState(monitor.id, 'Unavailable', 'off');
          }

          if (index < monitors.length - 1) {
            await delay(monitorInterRequestDelayMs);
          }
        }

        if (monitorActionStatus) {
          monitorActionStatus.textContent = 'Storage monitors updated.';
        }

        renderAllVirtualMonitorCombinedItems();
      })();

      try {
        await monitorRefreshInFlight;
      } finally {
        monitorRefreshInFlight = null;
      }
    }

    function renderSwitchItem(switchEntry) {
      const item = document.createElement('li');
      item.className = 'switch-item';
      item.dataset.realId = switchEntry.id;
      item.classList.add('real-switch-target');
      item.innerHTML = `
        <button type="button" class="real-power-in" data-real-id="${switchEntry.id}" title="Power in"></button>
        <div class="real-switch-meta">
          <img src="../Icons/Smartswitch.png" alt="Smart switch" class="smart-switch-icon" />
          <div class="switch-meta">
            <span class="switch-name">${switchEntry.name}</span>
            <span class="switch-id">ID: ${switchEntry.id}</span>
          </div>
        </div>
        <div class="switch-controls">
          <span class="switch-state" data-state="${switchEntry.state}">${switchEntry.stateLabel}</span>
          <button type="button" class="switch-action" data-id="${switchEntry.id}" data-state-value="true">On</button>
          <button type="button" class="switch-action" data-id="${switchEntry.id}" data-state-value="false">Off</button>
          <button type="button" class="switch-action real-switch-break-link" data-id="${switchEntry.id}">Break Link</button>
          <button type="button" class="switch-action real-switch-remove" data-id="${switchEntry.id}">Remove Switch</button>
        </div>`;

      switchList.appendChild(item);
    }

    function renderVirtualSwitchItem(switchEntry) {
      const linkedCount = getLinkedRealSwitchIds(switchEntry.id).length;
      const item = document.createElement('li');
      item.className = 'switch-item';
      item.dataset.virtualId = switchEntry.id;
      item.innerHTML = `
        <button type="button" class="virtual-power-out" data-virtual-id="${switchEntry.id}" title="Power out: click, then click a real switch power input"></button>
        <div class="switch-meta">
          <span class="switch-id">Name: ${switchEntry.name}</span>
          <span class="switch-id">Linked: ${linkedCount}</span>
        </div>
        <div class="switch-controls">
          <span class="switch-state" data-state="${switchEntry.isOn ? 'on' : 'off'}">${switchEntry.isOn ? 'On' : 'Off'}</span>
          <button type="button" class="switch-action virtual-switch-action" data-virtual-id="${switchEntry.id}" data-state-value="true">On</button>
          <button type="button" class="switch-action virtual-switch-action" data-virtual-id="${switchEntry.id}" data-state-value="false">Off</button>
          <button type="button" class="switch-action virtual-switch-break-all" data-virtual-id="${switchEntry.id}">Break All Links</button>
          <button type="button" class="switch-action virtual-switch-remove" data-virtual-id="${switchEntry.id}">Remove Switch</button>
        </div>`;

      virtualSwitchList.appendChild(item);
    }

    function addSwitchAndPersist(id, name) {
      const items = getSavedSwitches();
      const existingIndex = items.findIndex((entry) => entry.id === id);
      const entry = {
        id,
        name,
        state: 'unknown',
        stateLabel: 'Unknown'
      };

      if (existingIndex >= 0) {
        items[existingIndex] = entry;
      } else {
        items.push(entry);
      }

      saveSwitches(items);
      switchList.innerHTML = '';
      items.forEach(renderSwitchItem);
      renderLinkLines();
    }

    function updateSavedSwitchState(id, isOn) {
      const items = getSavedSwitches();
      const updatedItems = items.map((entry) => {
        if (entry.id !== id) {
          return entry;
        }

        return {
          ...entry,
          state: isOn ? 'on' : 'off',
          stateLabel: isOn ? 'On' : 'Off'
        };
      });

      saveSwitches(updatedItems);
    }

    function removeSavedSwitch(id) {
      const items = getSavedSwitches();
      const updatedItems = items.filter((entry) => entry.id !== id);

      saveSwitches(updatedItems);
      removeVirtualSwitchLinksByRealId(id);
      switchList.innerHTML = '';
      updatedItems.forEach(renderSwitchItem);
      renderVirtualSwitches();
      renderLinkLines();
      sanitizeMySystemPlacedDevices();
      renderMySystemPlacedDevices();
      renderMySystemDeviceTray();
    }

    function addMonitorAndPersist(id, name) {
      const items = getSavedMonitors();
      const existingIndex = items.findIndex((entry) => entry.id === id);
      const entry = {
        id,
        name
      };

      if (existingIndex >= 0) {
        items[existingIndex] = entry;
      } else {
        items.push(entry);
      }

      saveMonitors(items);
      monitorList.innerHTML = '';
      items.forEach(renderMonitorItem);
      loadMonitorItems(id).catch(() => {
        setMonitorItemState(id, 'Unavailable', 'off');
      });
      renderAllVirtualMonitorCombinedItems();
      renderMonitorLinkLines();
      renderMySystemDeviceTray();
    }

    function removeSavedMonitor(id) {
      const items = getSavedMonitors();
      const updatedItems = items.filter((entry) => entry.id !== id);
      saveMonitors(updatedItems);
      removeVirtualMonitorLinksByRealId(id);
      monitorItemsById.delete(String(id));
      monitorList.innerHTML = '';
      updatedItems.forEach(renderMonitorItem);
      renderVirtualMonitors();
      renderAllVirtualMonitorCombinedItems();
      renderMonitorLinkLines();
      sanitizeMySystemPlacedDevices();
      renderMySystemPlacedDevices();
      renderMySystemDeviceTray();
    }

    function addVirtualMonitorAndPersist(name) {
      const items = getSavedVirtualMonitors();
      const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;

      items.push({
        id,
        name
      });

      saveVirtualMonitors(items);
      renderVirtualMonitors();
      renderVirtualMonitorCombinedItems(id);
      renderMonitorLinkLines();
    }

    function removeVirtualMonitor(virtualMonitorId) {
      const items = getSavedVirtualMonitors();
      const updatedItems = items.filter((entry) => entry.id !== virtualMonitorId);
      saveVirtualMonitors(updatedItems);
      removeVirtualMonitorLinksByVirtualId(virtualMonitorId);
      renderVirtualMonitors();
      renderMonitorLinkLines();
    }

    function renderVirtualMonitors() {
      const virtualMonitors = getSavedVirtualMonitors();
      virtualMonitorList.innerHTML = '';
      virtualMonitors.forEach(renderVirtualMonitorItem);
      renderAllVirtualMonitorCombinedItems();
      renderMonitorLinkLines();
    }

    function addVirtualSwitchAndPersist(name) {
      const items = getSavedVirtualSwitches();
      const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;

      items.push({
        id,
        name,
        isOn: false
      });

      saveVirtualSwitches(items);
      renderVirtualSwitches();
      renderLinkLines();
    }

    function updateVirtualSwitchState(id, isOn) {
      const items = getSavedVirtualSwitches();
      const updatedItems = items.map((entry) => {
        if (entry.id !== id) {
          return entry;
        }

        return {
          ...entry,
          isOn
        };
      });

      saveVirtualSwitches(updatedItems);
      virtualSwitchList.innerHTML = '';
      updatedItems.forEach(renderVirtualSwitchItem);
      renderLinkLines();
    }

    function removeVirtualSwitch(id) {
      const items = getSavedVirtualSwitches();
      const updatedItems = items.filter((entry) => entry.id !== id);

      saveVirtualSwitches(updatedItems);
      removeVirtualSwitchLinksByVirtualId(id);
      virtualSwitchList.innerHTML = '';
      updatedItems.forEach(renderVirtualSwitchItem);
      renderLinkLines();
    }

    function renderVirtualSwitches() {
      const items = getSavedVirtualSwitches();
      virtualSwitchList.innerHTML = '';
      items.forEach(renderVirtualSwitchItem);
      renderLinkLines();
    }

    function updateRenderedRealSwitchState(id, isOn) {
      const item = switchList.querySelector(`.switch-item[data-real-id="${id}"]`);
      const stateBadge = item?.querySelector('.switch-state');
      if (!stateBadge) {
        return;
      }

      stateBadge.textContent = isOn ? 'On' : 'Off';
      stateBadge.dataset.state = isOn ? 'on' : 'off';
    }

    async function setRealSwitchState(id, isOn) {
      const response = await fetch('/api/switches/state', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          id,
          isOn
        })
      });

      const payload = await response.json();

      if (!response.ok || !payload.ok) {
        throw new Error(payload.message || 'Failed to update switch state.');
      }

      updateSavedSwitchState(id, payload.isOn);
      updateRenderedRealSwitchState(id, payload.isOn);
      return payload;
    }

    runBootTask(() => {
      const initialSwitches = getSavedSwitches();
      initialSwitches.forEach((entry) => {
        renderSwitchItem({
          state: 'unknown',
          stateLabel: 'Unknown',
          ...entry
        });
      });
    });

    runBootTask(() => {
      const initialVirtualSwitches = getSavedVirtualSwitches();
      initialVirtualSwitches.forEach(renderVirtualSwitchItem);
    });

    runBootTask(() => {
      const initialMonitors = getSavedMonitors();
      initialMonitors.forEach(renderMonitorItem);
    });

    runBootTask(() => {
      renderMySystemDeviceTray();
      renderMySystemPlacedDevices();
    });

    runBootTask(() => {
      const initialVirtualMonitors = getSavedVirtualMonitors();
      initialVirtualMonitors.forEach(renderVirtualMonitorItem);
      renderAllVirtualMonitorCombinedItems();
    });

    function applyPairedSwitchId(entityId) {
      if (entityId === null || entityId === undefined || entityId === '') {
        return;
      }

      switchIdInput.value = String(entityId);
      if (fcmStatus) {
        fcmStatus.textContent = `Pairing received. New switch ID: ${entityId}`;
      }
      setActiveTab('switches');
    }

    function applyPairedMonitorId(entityId) {
      if (entityId === null || entityId === undefined || entityId === '') {
        return;
      }

      monitorIdInput.value = String(entityId);
      if (monitorFcmStatus) {
        monitorFcmStatus.textContent = `Pairing received. New storage monitor ID: ${entityId}`;
      }
      setActiveTab('monitors');
    }

    function handleFcmMessage(payload) {
      if (!payload || typeof payload !== 'object') {
        return;
      }

      const eventType = String(payload.type || payload.event || '').toLowerCase();
      const entityId = extractEntityId(payload);
      const entityType = payload.entityType ?? payload?.data?.entityType;
      const entityName = String(payload.entityName || payload?.data?.entityName || '').toLowerCase();

      const isSmartSwitchPairingEvent = eventType.includes('smart') && eventType.includes('switch') && eventType.includes('pair');
      const isLikelySwitchEntityPairing = eventType.includes('pair') && (
        Number(entityType) === 1
        || entityName.includes('switch')
      );
      const isStorageMonitorPairingEvent = eventType.includes('storage') && eventType.includes('monitor') && eventType.includes('pair');
      const isLikelyStorageMonitorEntityPairing = eventType.includes('pair') && (
        Number(entityType) === 3
        || (entityName.includes('storage') && entityName.includes('monitor'))
      );

      if (isSmartSwitchPairingEvent || isLikelySwitchEntityPairing) {
        applyPairedSwitchId(entityId);
      }

      if (isStorageMonitorPairingEvent || isLikelyStorageMonitorEntityPairing) {
        applyPairedMonitorId(entityId);
      }
    }

    function clearFcmReconnectTimer() {
      if (!fcmReconnectTimer) {
        return;
      }

      clearTimeout(fcmReconnectTimer);
      fcmReconnectTimer = null;
    }

    function scheduleFcmReconnect() {
      if (fcmReconnectTimer) {
        return;
      }

      if (fcmStatus) {
        fcmStatus.textContent = 'FCM stream disconnected. Reconnecting...';
      }

      fcmReconnectTimer = setTimeout(() => {
        fcmReconnectTimer = null;
        connectFcmEventStream();
      }, 2000);
    }

    function connectFcmEventStream() {
      if (!window.EventSource) {
        return;
      }

      if (fcmEventSource) {
        fcmEventSource.close();
        fcmEventSource = null;
      }

      clearFcmReconnectTimer();

      const source = new EventSource('/api/fcm/events');
      fcmEventSource = source;

      source.onopen = () => {
        clearFcmReconnectTimer();
        if (fcmStatus) {
          fcmStatus.textContent = 'Connected. Waiting for smart switch pairing event...';
        }
        if (monitorFcmStatus) {
          monitorFcmStatus.textContent = 'Connected. Waiting for storage monitor pairing event...';
        }
      };

      source.onmessage = (event) => {
        try {
          handleFcmMessage(JSON.parse(event.data));
        } catch {
        }
      };

      source.addEventListener('smart-switch-pairing', (event) => {
        try {
          const payload = JSON.parse(event.data);
          const entityId = extractEntityId(payload);
          applyPairedSwitchId(entityId);
          window.dispatchEvent(new CustomEvent('fcm-smart-switch-pairing', {
            detail: {
              entityId,
              payload
            }
          }));
          handleFcmMessage(payload);
        } catch {
        }
      });

      source.addEventListener('entity-pairing', (event) => {
        try {
          handleFcmMessage(JSON.parse(event.data));
        } catch {
        }
      });

      source.addEventListener('storage-monitor-pairing', (event) => {
        try {
          const payload = JSON.parse(event.data);
          const entityId = extractEntityId(payload);
          applyPairedMonitorId(entityId);
          handleFcmMessage(payload);
        } catch {
        }
      });

      source.onerror = () => {
        if (fcmStatus) {
          fcmStatus.textContent = 'FCM stream disconnected. Reconnecting...';
        }
        if (monitorFcmStatus) {
          monitorFcmStatus.textContent = 'FCM stream disconnected. Reconnecting...';
        }

        if (!fcmEventSource || fcmEventSource.readyState === EventSource.CLOSED) {
          scheduleFcmReconnect();
        }
      };
    }

    runBootTask(() => connectFcmEventStream());

    mySystemUploadButton?.addEventListener('click', () => {
      mySystemUploadInput?.click();
    });

    mySystemUploadInput?.addEventListener('change', async (event) => {
      const [file] = Array.from(event.target.files || []);
      await handleMySystemImageSelection(file);
      event.target.value = '';
    });

    mySystemRemoveButton?.addEventListener('click', async () => {
      applyMySystemImage(null);

      try {
        await deleteMySystemImageRecord();
      } catch (error) {
        window.alert(`Image removed from view, but cache cleanup failed: ${error.message}`);
      }
    });

    mySystemMonitorCloseButton?.addEventListener('click', () => {
      closeMySystemMonitorWindow();
    });

    mySystemDeviceTray?.addEventListener('pointerdown', handleMySystemPointerDown);
    mySystemPlacedItemsLayer?.addEventListener('pointerdown', handleMySystemPointerDown);

    mySystemPlacedItemsLayer?.addEventListener('click', (event) => {
      const removeButton = event.target.closest('.my-system-node-remove');
      if (!removeButton) {
        const node = event.target.closest('.my-system-node');
        if (!node || mySystemSuppressNodeClick) {
          return;
        }

        const uniqueKey = node.dataset.uniqueKey || '';
        if (!uniqueKey.startsWith('monitor:')) {
          return;
        }

        const monitorId = uniqueKey.slice('monitor:'.length);
        openMonitorFromMySystem(monitorId);
        return;
      }

      removeMySystemPlacedDevice(removeButton.dataset.removeNode);
    });

    document.addEventListener('pointermove', (event) => {
      if (!mySystemDragState) {
        return;
      }

      updateMySystemDragGhost(event.clientX, event.clientY);
    });

    document.addEventListener('pointerup', (event) => {
      finishMySystemDrag(event.clientX, event.clientY);
    });

    document.addEventListener('pointercancel', () => {
      cancelMySystemDrag();
    });

    runBootTask(() => loadMySystemImage());

    window.addEventListener('fcm-smart-switch-pairing', (event) => {
      applyPairedSwitchId(event.detail?.entityId);
    });

    window.addEventListener('beforeunload', () => {
      clearFcmReconnectTimer();
      ensureMapAutoRefresh(false);
      if (fcmEventSource) {
        fcmEventSource.close();
        fcmEventSource = null;
      }
      if (mySystemImageObjectUrl) {
        URL.revokeObjectURL(mySystemImageObjectUrl);
        mySystemImageObjectUrl = null;
      }
    });

    mapCanvas?.addEventListener('click', async (event) => {
      if (!mapRenderContext || !mapDebugToggle?.checked) {
        return;
      }

      const rect = mapCanvas.getBoundingClientRect();
      const canvasX = event.clientX - rect.left;
      const canvasY = event.clientY - rect.top;
      const rawX = projectCanvasToMapX(canvasX, mapRenderContext);
      const rawY = projectCanvasToMapY(canvasY, mapRenderContext);
      const marginAdjustedX = rawX + mapRenderContext.oceanMargin;
      const marginAdjustedY = rawY + mapRenderContext.oceanMargin;

      const vendingMarkers = (mapRenderContext.markers || []).filter((marker) => marker?.type === 'vendingMachine');

      let nearestVending = null;
      vendingMarkers.forEach((marker) => {
        const dx = Number(marker.x) - rawX;
        const dy = Number(marker.y) - rawY;
        const distance = Math.sqrt((dx * dx) + (dy * dy));

        if (!nearestVending || distance < nearestVending.distance) {
          nearestVending = {
            id: marker.id,
            x: Number(marker.x),
            y: Number(marker.y),
            distance
          };
        }
      });

      mapDebugLastClick = {
        canvasX,
        canvasY,
        rawX,
        rawY,
        marginAdjustedX,
        marginAdjustedY,
        nearestVending
      };

      updateMapDebugOverlay();
      if (latestMapPayload) {
        await renderMap(latestMapPayload);
      }
    });

    mapDebugToggle?.addEventListener('change', async () => {
      if (!mapDebugToggle.checked) {
        mapDebugLastClick = null;
      }

      updateMapDebugOverlay();
      if (latestMapPayload) {
        await renderMap(latestMapPayload);
      }
    });

    switchForm.addEventListener('submit', (event) => {
      event.preventDefault();

      const id = switchIdInput.value.trim();
      const name = switchNameInput.value.trim() || `Smart Switch ${id}`;

      if (!id || !name) {
        return;
      }

      try {
        addSwitchAndPersist(id, name);

        switchIdInput.value = '';
        switchNameInput.value = '';
        if (fcmStatus) {
          fcmStatus.textContent = 'Waiting for smart switch pairing event...';
        }
        if (switchActionStatus) {
          switchActionStatus.textContent = `Saved switch ${id}.`;
        }
      } catch (error) {
        if (switchActionStatus) {
          switchActionStatus.textContent = `Failed to save switch ${id}: ${error.message}`;
        }
      }
    });

    monitorForm.addEventListener('submit', (event) => {
      event.preventDefault();

      const id = monitorIdInput.value.trim();
      const name = monitorNameInput.value.trim() || `Storage Monitor ${id}`;

      if (!id || !name) {
        return;
      }

      try {
        addMonitorAndPersist(id, name);

        monitorIdInput.value = '';
        monitorNameInput.value = '';
        if (monitorFcmStatus) {
          monitorFcmStatus.textContent = 'Waiting for storage monitor pairing event...';
        }
        if (monitorActionStatus) {
          monitorActionStatus.textContent = `Saved storage monitor ${id}.`;
        }
      } catch (error) {
        if (monitorActionStatus) {
          monitorActionStatus.textContent = `Failed to save storage monitor ${id}: ${error.message}`;
        }
      }
    });

    virtualMonitorForm.addEventListener('submit', (event) => {
      event.preventDefault();

      const name = virtualMonitorNameInput.value.trim();
      if (!name) {
        return;
      }

      addVirtualMonitorAndPersist(name);
      virtualMonitorNameInput.value = '';
      if (monitorActionStatus) {
        monitorActionStatus.textContent = `Created virtual box \"${name}\".`;
      }
    });

    monitorList.addEventListener('click', (event) => {
      const connectButton = event.target.closest('.monitor-real-in');
      if (connectButton) {
        const monitorId = connectButton.dataset.monitorId;
        if (!monitorId || !activeVirtualMonitorWiringId) {
          return;
        }

        const linked = addVirtualMonitorLink(activeVirtualMonitorWiringId, monitorId);
        renderVirtualMonitors();
        renderMonitorLinkLines();
        if (monitorActionStatus) {
          monitorActionStatus.textContent = linked
            ? `Linked virtual box to storage monitor ${monitorId}.`
            : `Virtual box is already linked to storage monitor ${monitorId}.`;
        }
        clearMonitorWiringMode();
        return;
      }

      const toggleButton = event.target.closest('.monitor-toggle');
      if (toggleButton) {
        const monitorId = toggleButton.dataset.id;
        if (!monitorId) {
          return;
        }

        const monitorItem = monitorList.querySelector(`.switch-item[data-monitor-id="${monitorId}"]`);
        const isCollapsed = monitorItem?.classList.contains('is-collapsed');
        setMonitorCollapsed(monitorId, !isCollapsed);
        return;
      }

      const refreshButton = event.target.closest('.monitor-refresh');
      if (refreshButton) {
        const monitorId = refreshButton.dataset.id;
        if (!monitorId) {
          return;
        }

        loadMonitorItems(monitorId).catch((error) => {
          setMonitorItemState(monitorId, 'Unavailable', 'off');
          if (monitorActionStatus) {
            monitorActionStatus.textContent = `Failed to refresh monitor ${monitorId}: ${error.message}`;
          }
        });
        return;
      }

      const breakLinkButton = event.target.closest('.monitor-break-link');
      if (breakLinkButton) {
        const monitorId = breakLinkButton.dataset.id;
        if (!monitorId) {
          return;
        }

        if (activeVirtualMonitorWiringId) {
          const removed = removeVirtualMonitorLink(activeVirtualMonitorWiringId, monitorId);
          renderVirtualMonitors();
          renderMonitorLinkLines();
          if (monitorActionStatus) {
            monitorActionStatus.textContent = removed
              ? `Link removed between selected virtual box and monitor ${monitorId}.`
              : `No link found between selected virtual box and monitor ${monitorId}.`;
          }
          clearMonitorWiringMode();
          return;
        }

        const linkedVirtualIds = getLinkedVirtualMonitorIds(monitorId);
        if (linkedVirtualIds.length === 1) {
          removeVirtualMonitorLink(linkedVirtualIds[0], monitorId);
          renderVirtualMonitors();
          renderMonitorLinkLines();
          if (monitorActionStatus) {
            monitorActionStatus.textContent = `Link removed for monitor ${monitorId}.`;
          }
          return;
        }

        if (linkedVirtualIds.length > 1) {
          if (monitorActionStatus) {
            monitorActionStatus.textContent = 'Select a virtual box connector first, then click Break Link on this monitor.';
          }
          return;
        }

        if (monitorActionStatus) {
          monitorActionStatus.textContent = `No links found for monitor ${monitorId}.`;
        }
        return;
      }

      const removeButton = event.target.closest('.monitor-remove');
      if (!removeButton) {
        return;
      }

      const monitorId = removeButton.dataset.id;
      if (!monitorId) {
        return;
      }

      if (removeButton.dataset.confirm !== 'true') {
        setPendingRemoveButton(removeButton);
        if (monitorActionStatus) {
          monitorActionStatus.textContent = '';
        }
        return;
      }

      removeSavedMonitor(monitorId);
      clearPendingRemoveButton();
      if (monitorActionStatus) {
        monitorActionStatus.textContent = `Removed storage monitor ${monitorId}.`;
      }
    });

    virtualMonitorList.addEventListener('click', (event) => {
      const connectButton = event.target.closest('.monitor-virtual-out');
      if (connectButton) {
        const virtualMonitorId = connectButton.dataset.virtualMonitorId;
        if (!virtualMonitorId) {
          return;
        }

        if (activeVirtualMonitorWiringId === virtualMonitorId) {
          clearMonitorWiringMode();
          if (monitorActionStatus) {
            monitorActionStatus.textContent = 'Virtual box wiring mode cleared.';
          }
        } else {
          startMonitorWiringFromVirtual(virtualMonitorId);
          if (monitorActionStatus) {
            monitorActionStatus.textContent = 'Virtual box wiring active. Click a real monitor connector to link.';
          }
        }

        return;
      }

      const breakAllButton = event.target.closest('.virtual-monitor-break-all');
      if (breakAllButton) {
        const virtualMonitorId = breakAllButton.dataset.id;
        if (!virtualMonitorId) {
          return;
        }

        removeVirtualMonitorLinksByVirtualId(virtualMonitorId);
        renderVirtualMonitors();
        renderMonitorLinkLines();
        if (activeVirtualMonitorWiringId === virtualMonitorId) {
          clearMonitorWiringMode();
        }
        if (monitorActionStatus) {
          monitorActionStatus.textContent = 'All links removed for virtual box.';
        }
        return;
      }

      const removeButton = event.target.closest('.virtual-monitor-remove');
      if (!removeButton) {
        return;
      }

      const virtualMonitorId = removeButton.dataset.id;
      if (!virtualMonitorId) {
        return;
      }

      if (removeButton.dataset.confirm !== 'true') {
        setPendingRemoveButton(removeButton);
        return;
      }

      removeVirtualMonitor(virtualMonitorId);
      clearPendingRemoveButton();
      if (activeVirtualMonitorWiringId === virtualMonitorId) {
        clearMonitorWiringMode();
      }
      if (monitorActionStatus) {
        monitorActionStatus.textContent = 'Virtual box removed.';
      }
    });

    virtualSwitchForm.addEventListener('submit', (event) => {
      event.preventDefault();

      const name = virtualSwitchNameInput.value.trim();
      if (!name) {
        return;
      }

      addVirtualSwitchAndPersist(name);
      virtualSwitchNameInput.value = '';
    });

    switchList.addEventListener('click', async (event) => {
      const removeButton = event.target.closest('.real-switch-remove');
      if (removeButton) {
        const switchId = removeButton.dataset.id;
        if (!switchId) {
          return;
        }

        if (removeButton.dataset.confirm !== 'true') {
          setPendingRemoveButton(removeButton);
          switchActionStatus.textContent = '';
          return;
        }

        removeSavedSwitch(switchId);
        clearPendingRemoveButton();
        switchActionStatus.textContent = `Removed switch ${switchId}.`;
        return;
      }

      const button = event.target.closest('.switch-action');
      if (!button) {
        return;
      }

      if (button.dataset.stateValue !== 'true' && button.dataset.stateValue !== 'false') {
        return;
      }

      const switchId = button.dataset.id;
      const isOn = button.dataset.stateValue === 'true';
      const item = button.closest('.switch-item');
      const stateBadge = item?.querySelector('.switch-state');

      if (!switchId || !stateBadge) {
        return;
      }

      switchActionStatus.textContent = `Updating switch ${switchId}...`;

      try {
        const payload = await setRealSwitchState(switchId, isOn);
        const currentState = payload.isOn ? 'On' : 'Off';
        switchActionStatus.textContent = `Switch ${switchId} is now ${currentState}.`;
      } catch (error) {
        switchActionStatus.textContent = `Failed to update switch ${switchId}: ${error.message}`;
      }
    });

    virtualSwitchList.addEventListener('click', async (event) => {
      const powerOutButton = event.target.closest('.virtual-power-out');
      if (powerOutButton) {
        const virtualId = powerOutButton.dataset.virtualId;
        if (!virtualId) {
          return;
        }

        if (activeWiringVirtualId === virtualId) {
          clearWiringMode();
        } else {
          startWiringFromVirtual(virtualId);
          switchActionStatus.textContent = 'Wiring mode active. Click a real switch power input to connect.';
        }
        return;
      }

      const breakAllButton = event.target.closest('.virtual-switch-break-all');
      if (breakAllButton) {
        const virtualId = breakAllButton.dataset.virtualId;
        if (!virtualId) {
          return;
        }

        removeVirtualSwitchLinksByVirtualId(virtualId);
        renderVirtualSwitches();
        switchActionStatus.textContent = 'All links removed for virtual switch.';
        if (activeWiringVirtualId === virtualId) {
          clearWiringMode();
        }
        return;
      }

      const removeButton = event.target.closest('.virtual-switch-remove');
      if (removeButton) {
        const switchId = removeButton.dataset.virtualId;
        if (!switchId) {
          return;
        }

        if (removeButton.dataset.confirm !== 'true') {
          setPendingRemoveButton(removeButton);
          return;
        }

        removeVirtualSwitch(switchId);
        clearPendingRemoveButton();
        return;
      }

      const actionButton = event.target.closest('.virtual-switch-action');
      if (!actionButton) {
        return;
      }

      const switchId = actionButton.dataset.virtualId;
      const isOn = actionButton.dataset.stateValue === 'true';
      if (!switchId) {
        return;
      }

      updateVirtualSwitchState(switchId, isOn);

      const linkedRealIds = getLinkedRealSwitchIds(switchId);
      if (linkedRealIds.length === 0) {
        return;
      }

      for (let index = 0; index < linkedRealIds.length; index += 1) {
        const realId = linkedRealIds[index];

        try {
          await setRealSwitchState(realId, isOn);
        } catch {
        }

        if (index < linkedRealIds.length - 1) {
          await delay(virtualSwitchInteractionDelayMs);
        }
      }
    });

    switchList.addEventListener('click', (event) => {
      const powerInButton = event.target.closest('.real-power-in');
      if (powerInButton) {
        const realSwitchId = powerInButton.dataset.realId;
        if (!realSwitchId || !activeWiringVirtualId) {
          return;
        }

        const linked = addVirtualSwitchLink(activeWiringVirtualId, realSwitchId);
        renderVirtualSwitches();
        renderLinkLines();
        switchActionStatus.textContent = linked
          ? `Linked virtual switch to real switch ${realSwitchId}.`
          : `Virtual switch is already linked to real switch ${realSwitchId}.`;
        clearWiringMode();
        return;
      }

      const breakLinkButton = event.target.closest('.real-switch-break-link');
      if (!breakLinkButton) {
        return;
      }

      const realSwitchId = breakLinkButton.dataset.id;
      if (!realSwitchId) {
        return;
      }

      if (activeWiringVirtualId) {
        const removed = removeVirtualToRealLink(activeWiringVirtualId, realSwitchId);
        renderVirtualSwitches();
        renderLinkLines();
        switchActionStatus.textContent = removed
          ? `Link removed between selected virtual switch and real switch ${realSwitchId}.`
          : `No link found between selected virtual switch and real switch ${realSwitchId}.`;
        clearWiringMode();
        return;
      }

      const linkedVirtualIds = getLinkedVirtualIdsForReal(realSwitchId);
      if (linkedVirtualIds.length === 1) {
        removeVirtualToRealLink(linkedVirtualIds[0], realSwitchId);
        renderVirtualSwitches();
        renderLinkLines();
        switchActionStatus.textContent = `Link removed for real switch ${realSwitchId}.`;
        return;
      }

      if (linkedVirtualIds.length > 1) {
        switchActionStatus.textContent = 'Select a virtual power-out first, then click Break Link on this real switch.';
        return;
      }

      switchActionStatus.textContent = `No links found for real switch ${realSwitchId}.`;
    });

    document.addEventListener('mousemove', (event) => {
      if (!activeWiringVirtualId || !switchWiringArea) {
        return;
      }

      const areaRect = switchWiringArea.getBoundingClientRect();
      previewCursorPoint = {
        x: event.clientX - areaRect.left,
        y: event.clientY - areaRect.top
      };
      renderLinkLines();
    });

    document.addEventListener('click', (event) => {
      const isRemoveButtonClick = event.target.closest('.real-switch-remove, .virtual-switch-remove, .monitor-remove, .virtual-monitor-remove');
      if (isRemoveButtonClick) {
        return;
      }

      const isWiringClick = event.target.closest('.virtual-power-out, .real-power-in');
      const isInsideWiringArea = switchWiringArea ? switchWiringArea.contains(event.target) : false;
      if (!isWiringClick && !isInsideWiringArea && activeWiringVirtualId) {
        clearWiringMode();
      }

      // Only clear monitor wiring mode if NOT clicking inside monitor wiring area and Monitors tab is not active
      const isMonitorWiringClick = event.target.closest('.monitor-virtual-out, .monitor-real-in');
      const isInsideMonitorArea = monitorList ? monitorList.contains(event.target) : false;
      const isInsideVirtualMonitorArea = virtualMonitorList ? virtualMonitorList.contains(event.target) : false;
      const isInsideMonitorWiringArea = monitorWiringArea ? monitorWiringArea.contains(event.target) : false;
      const isMonitorsTabActive = document.querySelector('.tab-panel[data-panel="monitors"]')?.classList.contains('active');
      if (!isMonitorWiringClick && !isInsideMonitorArea && !isInsideVirtualMonitorArea && !isInsideMonitorWiringArea && activeVirtualMonitorWiringId && isMonitorsTabActive) {
        clearMonitorWiringMode();
      }

      clearPendingRemoveButton();
    });

    document.addEventListener('mousemove', (event) => {
      if (!activeVirtualMonitorWiringId || !monitorWiringArea) {
        return;
      }
      const areaRect = monitorWiringArea.getBoundingClientRect();
      previewMonitorCursorPoint = {
        x: event.clientX - areaRect.left,
        y: event.clientY - areaRect.top
      };
      renderMonitorLinkLines();
    });

    if (window.ResizeObserver) {
      const switchResizeObserver = new ResizeObserver(() => {
        scheduleRenderLinkLines();
      });

      [switchWiringArea, switchList, virtualSwitchList].forEach((element) => {
        if (element) {
          switchResizeObserver.observe(element);
        }
      });

      const monitorResizeObserver = new ResizeObserver(() => {
        scheduleRenderMonitorLinkLines();
      });

      [monitorWiringArea, monitorList, virtualMonitorList].forEach((element) => {
        if (element) {
          monitorResizeObserver.observe(element);
        }
      });
    }

    if (window.MutationObserver) {
      const switchMutationObserver = new MutationObserver(() => {
        scheduleRenderLinkLines();
      });

      [switchList, virtualSwitchList].forEach((element) => {
        if (element) {
          switchMutationObserver.observe(element, {
            attributes: true,
            childList: true,
            subtree: true,
            attributeFilter: ['class', 'style']
          });
        }
      });

      const monitorMutationObserver = new MutationObserver(() => {
        scheduleRenderMonitorLinkLines();
      });

      [monitorList, virtualMonitorList].forEach((element) => {
        if (element) {
          monitorMutationObserver.observe(element, {
            attributes: true,
            childList: true,
            subtree: true,
            attributeFilter: ['class', 'style']
          });
        }
      });
    }

    window.addEventListener('resize', () => {
      scheduleRenderLinkLines();
      scheduleRenderMonitorLinkLines();
    });

    window.addEventListener('scroll', () => {
      scheduleRenderLinkLines();
      scheduleRenderMonitorLinkLines();
    }, true);

    runBootTask(() => loadInfoEvents());
    runBootTask(() => loadTeamStatus());
    runBootTask(() => scheduleRenderLinkLines());
    runBootTask(() => scheduleRenderMonitorLinkLines());
    setInterval(loadInfoEvents, 30000);
    setInterval(loadTeamStatus, 30000);
    })();
  
