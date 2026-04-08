
    (() => {
      const uploadButton = document.getElementById('mySystemUploadButton');
      const uploadInput = document.getElementById('mySystemUploadInput');
      const removeButton = document.getElementById('mySystemRemoveButton');
      const imageLayer = document.getElementById('mySystemImageLayer');
      const databaseName = 'rustplus-web-ui';
      const storeName = 'assets';
      const recordKey = 'my-system-background';
      const imageMaxBytes = 20 * 1024 * 1024;
      let imageObjectUrl = null;
      let databasePromise = null;

      if (!uploadButton || !uploadInput || !removeButton || !imageLayer) {
        return;
      }

      function syncControls(hasImage) {
        uploadButton.hidden = hasImage;
        removeButton.hidden = !hasImage;
      }

      function revokeImageUrl() {
        if (!imageObjectUrl) {
          return;
        }

        URL.revokeObjectURL(imageObjectUrl);
        imageObjectUrl = null;
      }

      function applyImageRecord(record) {
        revokeImageUrl();

        if (!record?.blob) {
          imageLayer.style.backgroundImage = 'none';
          imageLayer.dataset.hasImage = 'false';
          syncControls(false);
          return;
        }

        imageObjectUrl = URL.createObjectURL(record.blob);
        imageLayer.style.backgroundImage = `url("${imageObjectUrl}")`;
        imageLayer.dataset.hasImage = 'true';
        syncControls(true);
      }

      function getDatabase() {
        if (!('indexedDB' in window)) {
          return Promise.reject(new Error('IndexedDB is not available in this browser.'));
        }

        if (!databasePromise) {
          databasePromise = new Promise((resolve, reject) => {
            const request = window.indexedDB.open(databaseName, 1);

            request.onupgradeneeded = () => {
              const database = request.result;
              if (!database.objectStoreNames.contains(storeName)) {
                database.createObjectStore(storeName);
              }
            };

            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error || new Error('Failed to open image cache.'));
          });
        }

        return databasePromise;
      }

      async function readImageRecord() {
        const database = await getDatabase();

        return new Promise((resolve, reject) => {
          const transaction = database.transaction(storeName, 'readonly');
          const store = transaction.objectStore(storeName);
          const request = store.get(recordKey);

          request.onsuccess = () => resolve(request.result || null);
          request.onerror = () => reject(request.error || new Error('Failed to read cached image.'));
        });
      }

      async function writeImageRecord(record) {
        const database = await getDatabase();

        return new Promise((resolve, reject) => {
          const transaction = database.transaction(storeName, 'readwrite');
          const store = transaction.objectStore(storeName);
          const request = store.put(record, recordKey);

          request.onsuccess = () => resolve();
          request.onerror = () => reject(request.error || new Error('Failed to cache image.'));
        });
      }

      async function deleteImageRecord() {
        const database = await getDatabase();

        return new Promise((resolve, reject) => {
          const transaction = database.transaction(storeName, 'readwrite');
          const store = transaction.objectStore(storeName);
          const request = store.delete(recordKey);

          request.onsuccess = () => resolve();
          request.onerror = () => reject(request.error || new Error('Failed to remove cached image.'));
        });
      }

      uploadButton.addEventListener('click', () => {
        uploadInput.click();
      });

      uploadInput.addEventListener('change', async (event) => {
        const [file] = Array.from(event.target.files || []);
        event.target.value = '';

        if (!file) {
          return;
        }

        if (!file.type.startsWith('image/')) {
          window.alert('Please choose an image file.');
          return;
        }

        if (file.size > imageMaxBytes) {
          window.alert('Image is too large. Maximum size is 20 MB.');
          return;
        }

        const record = {
          name: file.name,
          size: file.size,
          type: file.type,
          savedAt: new Date().toISOString(),
          blob: file
        };

        applyImageRecord(record);

        try {
          await writeImageRecord(record);
        } catch (error) {
          console.error(error);
        }
      });

      removeButton.addEventListener('click', async () => {
        applyImageRecord(null);

        try {
          await deleteImageRecord();
        } catch (error) {
          console.error(error);
        }
      });

      readImageRecord()
        .then((record) => {
          applyImageRecord(record);
        })
        .catch(() => {
          applyImageRecord(null);
        });

      window.addEventListener('beforeunload', () => {
        revokeImageUrl();
      });
    })();
  
