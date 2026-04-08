
    (() => {
      if (window.__rustplusFallbackPairingBridgeStarted || !window.EventSource) {
        return;
      }

      window.__rustplusFallbackPairingBridgeStarted = true;

      const switchIdInput = document.getElementById('switchId');
      const monitorIdInput = document.getElementById('monitorId');
      const fcmStatus = document.getElementById('fcmStatus');
      const monitorFcmStatus = document.getElementById('monitorFcmStatus');
      const liveObserver = window.__rustplusLiveObserver;

      function activateTab(tabName) {
        if (typeof window.__rustplusActivateFallbackTab === 'function') {
          window.__rustplusActivateFallbackTab(tabName);
        }
      }

      function extractEntityId(payload) {
        const candidates = [
          payload?.entityId,
          payload?.id,
          payload?.entityID,
          payload?.entity_id,
          payload?.data?.entityId,
          payload?.data?.id,
          payload?.Data?.EntityId,
          payload?.Data
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

      const source = new EventSource('/api/fcm/events');

      source.onopen = () => {
        liveObserver?.setConnection('connected', 'Connected to /api/fcm/events');
        if (fcmStatus) {
          fcmStatus.textContent = 'Connected. Waiting for smart switch pairing event...';
        }
        if (monitorFcmStatus) {
          monitorFcmStatus.textContent = 'Connected. Waiting for storage monitor pairing event...';
        }
      };

      source.addEventListener('connected', () => {
        liveObserver?.recordEvent('connected', {
          detail: 'Server acknowledged SSE stream.',
          state: 'connected'
        });
      });

      source.addEventListener('ping', () => {
        liveObserver?.recordEvent('ping', {
          detail: 'Keepalive received.',
          state: 'connected'
        });
      });

      source.addEventListener('entity-pairing', (event) => {
        try {
          const payload = JSON.parse(event.data);
          const entityId = extractEntityId(payload);
          liveObserver?.recordEvent('entity-pairing', {
            entityId,
            detail: 'Fallback observer received entity pairing.',
            state: 'active'
          });

          const entityType = Number(payload?.entityType ?? payload?.data?.entityType);
          const entityName = String(payload?.entityName ?? payload?.data?.entityName ?? '').toLowerCase();

          if (entityId && (entityType === 1 || entityName.includes('switch'))) {
            if (switchIdInput) {
              switchIdInput.value = entityId;
            }
            if (fcmStatus) {
              fcmStatus.textContent = `Pairing received. New switch ID: ${entityId}`;
            }
            activateTab('switches');
          }

          if (entityId && (entityType === 3 || (entityName.includes('storage') && entityName.includes('monitor')))) {
            if (monitorIdInput) {
              monitorIdInput.value = entityId;
            }
            if (monitorFcmStatus) {
              monitorFcmStatus.textContent = `Pairing received. New storage monitor ID: ${entityId}`;
            }
            activateTab('monitors');
          }
        } catch {
          liveObserver?.note('Failed to parse entity-pairing payload.', 'warning');
        }
      });

      source.addEventListener('smart-switch-pairing', (event) => {
        try {
          const payload = JSON.parse(event.data);
          const entityId = extractEntityId(payload);
          if (!entityId) {
            liveObserver?.note('Received smart-switch-pairing without an entity ID.', 'warning');
            return;
          }

          if (switchIdInput) {
            switchIdInput.value = entityId;
          }
          if (fcmStatus) {
            fcmStatus.textContent = `Pairing received. New switch ID: ${entityId}`;
          }
          liveObserver?.recordEvent('smart-switch-pairing', {
            entityId,
            detail: 'Switch field updated.',
            state: 'active'
          });
          activateTab('switches');
        } catch {
          liveObserver?.note('Failed to parse smart-switch-pairing payload.', 'warning');
        }
      });

      source.addEventListener('storage-monitor-pairing', (event) => {
        try {
          const payload = JSON.parse(event.data);
          const entityId = extractEntityId(payload);
          if (!entityId) {
            liveObserver?.note('Received storage-monitor-pairing without an entity ID.', 'warning');
            return;
          }

          if (monitorIdInput) {
            monitorIdInput.value = entityId;
          }
          if (monitorFcmStatus) {
            monitorFcmStatus.textContent = `Pairing received. New storage monitor ID: ${entityId}`;
          }
          liveObserver?.recordEvent('storage-monitor-pairing', {
            entityId,
            detail: 'Monitor field updated.',
            state: 'active'
          });
          activateTab('monitors');
        } catch {
          liveObserver?.note('Failed to parse storage-monitor-pairing payload.', 'warning');
        }
      });

      source.onerror = () => {
        liveObserver?.setConnection('disconnected', 'Stream disconnected. Browser is retrying...');
        if (fcmStatus) {
          fcmStatus.textContent = 'FCM stream disconnected. Reconnecting...';
        }
        if (monitorFcmStatus) {
          monitorFcmStatus.textContent = 'FCM stream disconnected. Reconnecting...';
        }
      };
    })();
  
