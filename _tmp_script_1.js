
    (() => {
      const panel = document.getElementById('liveObserverPanel');
      const dot = document.getElementById('liveObserverDot');
      const connection = document.getElementById('liveObserverConnection');
      const eventLabel = document.getElementById('liveObserverEvent');
      const entityLabel = document.getElementById('liveObserverEntity');
      const timestampLabel = document.getElementById('liveObserverTimestamp');
      const log = document.getElementById('liveObserverLog');
      const maxEntries = 6;

      if (!panel || !dot || !connection || !eventLabel || !entityLabel || !timestampLabel || !log) {
        return;
      }

      function formatTimestamp(date) {
        return date.toLocaleTimeString([], {
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit'
        });
      }

      function trimLog() {
        while (log.children.length > maxEntries) {
          log.removeChild(log.lastElementChild);
        }
      }

      function clearEmptyState() {
        const emptyEntry = log.querySelector('.live-observer-log-empty');
        if (emptyEntry) {
          emptyEntry.remove();
        }
      }

      function pushLogEntry(message, state) {
        clearEmptyState();
        const entry = document.createElement('li');
        entry.className = 'live-observer-log-entry';
        if (state) {
          entry.dataset.state = state;
        }
        entry.textContent = `${formatTimestamp(new Date())}  ${message}`;
        log.prepend(entry);
        trimLog();
      }

      function setConnectionState(state, message) {
        dot.dataset.state = state;
        connection.textContent = message;
      }

      function recordEvent(eventName, options = {}) {
        const now = new Date();
        const entityId = options.entityId ? String(options.entityId) : '-';
        const detail = options.detail ? ` ${options.detail}` : '';

        eventLabel.textContent = eventName;
        entityLabel.textContent = entityId;
        timestampLabel.textContent = formatTimestamp(now);
        pushLogEntry(`${eventName}${entityId !== '-' ? ` ${entityId}` : ''}${detail}`, options.state || 'active');
      }

      function extractObserverEntityId(payload) {
        const candidates = [
          payload?.entityId,
          payload?.id,
          payload?.entityID,
          payload?.entity_id,
          payload?.data?.entityId,
          payload?.data?.id,
          payload?.payload?.entityId,
          payload?.payload?.id
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

      async function startDebugObserver() {
        const seenEvents = new Set();
        const switchIdInput = document.getElementById('switchId');
        const monitorIdInput = document.getElementById('monitorId');
        const fcmStatus = document.getElementById('fcmStatus');
        const monitorFcmStatus = document.getElementById('monitorFcmStatus');

        function buildEventKey(event) {
          return [
            event?.occurredAtUtc || '',
            event?.type || '',
            extractObserverEntityId(event?.payload) || ''
          ].join('|');
        }

        function isSwitchPairingEvent(event) {
          const entityType = Number(event?.payload?.entityType);
          const entityName = String(event?.payload?.entityName || '').toLowerCase();
          return event?.type === 'smart-switch-pairing'
            || (event?.type === 'entity-pairing' && (entityType === 1 || entityName.includes('switch')));
        }

        function isMonitorPairingEvent(event) {
          const entityType = Number(event?.payload?.entityType);
          const entityName = String(event?.payload?.entityName || '').toLowerCase();
          return event?.type === 'storage-monitor-pairing'
            || (event?.type === 'entity-pairing' && (entityType === 3 || (entityName.includes('storage') && entityName.includes('monitor'))));
        }

        async function pollDebugStatus() {
          try {
            const response = await fetch('/api/debug/status', { cache: 'no-store' });
            if (!response.ok) {
              throw new Error(`Debug status returned ${response.status}`);
            }

            const payload = await response.json();
            const recentEvents = Array.isArray(payload?.recentEvents) ? payload.recentEvents : [];

            for (const recentEvent of recentEvents) {
              const key = buildEventKey(recentEvent);
              if (seenEvents.has(key)) {
                continue;
              }

              seenEvents.add(key);

              const entityId = extractObserverEntityId(recentEvent?.payload);
              recordEvent(`backend:${recentEvent?.type || 'unknown'}`, {
                entityId,
                detail: 'Seen via /api/debug/status.',
                state: 'active'
              });

              if (isSwitchPairingEvent(recentEvent) && entityId) {
                if (switchIdInput) {
                  switchIdInput.value = entityId;
                }
                if (fcmStatus) {
                  fcmStatus.textContent = `Pairing received from backend observer. New switch ID: ${entityId}`;
                }
                if (typeof window.__rustplusActivateFallbackTab === 'function') {
                  window.__rustplusActivateFallbackTab('switches');
                }
              }

              if (isMonitorPairingEvent(recentEvent) && entityId) {
                if (monitorIdInput) {
                  monitorIdInput.value = entityId;
                }
                if (monitorFcmStatus) {
                  monitorFcmStatus.textContent = `Pairing received from backend observer. New storage monitor ID: ${entityId}`;
                }
                if (typeof window.__rustplusActivateFallbackTab === 'function') {
                  window.__rustplusActivateFallbackTab('monitors');
                }
              }
            }
          } catch (error) {
            pushLogEntry(`Debug observer poll failed: ${error?.message || 'unknown error'}`, 'warning');
          }
        }

        await pollDebugStatus();
        window.setInterval(() => {
          pollDebugStatus();
        }, 2000);
      }

      window.__rustplusLiveObserver = {
        setConnection(state, message) {
          setConnectionState(state, message);
          pushLogEntry(message, state);
        },
        recordEvent,
        note(message, state = 'info') {
          timestampLabel.textContent = formatTimestamp(new Date());
          pushLogEntry(message, state);
        }
      };

      setConnectionState('connecting', 'Connecting...');
      startDebugObserver().catch(() => {
      });
    })();
  
