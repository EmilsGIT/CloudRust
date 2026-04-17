FROM node:20-bookworm

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        fluxbox \
        gnupg \
        python3 \
        wget \
        websockify \
        x11vnc \
        xvfb \
    && wget -q -O - https://dl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/google-chrome.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends google-chrome-stable \
    && printf '#!/bin/sh\nexec /usr/bin/google-chrome-stable --disable-dev-shm-usage --disable-gpu --disable-crash-reporter --disable-crashpad --no-first-run --no-default-browser-check --window-size=1440,900 "$@"\n' > /usr/local/bin/google-chrome-login-handler \
    && chmod +x /usr/local/bin/google-chrome-login-handler \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY package.json ./
RUN npm install --omit=dev

COPY src ./src

RUN mkdir -p /app/data \
    && chown -R node:node /app

USER node

EXPOSE 6061

CMD ["npm", "start"]