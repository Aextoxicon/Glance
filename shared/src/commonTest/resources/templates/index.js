import { readFile } from 'fs/promises';
import path from 'path';

const CONFIG = {
    version: '1.0.0',
    debug: false,
    ports: {
        http: 8080,
        ws: 3001,
    },
};

class Logger {
    constructor(level) {
        this.level = level;
        this.logs = [];
    }

    log(message) {
        const entry = `[${new Date().toISOString()}] ${message}`;
        this.logs.push(entry);
        if (this.level === 'verbose') {
            console.log(entry);
        }
    }

    static create(level = 'info') {
        return new Logger(level);
    }
}

async function loadConfig(filePath) {
    const content = await readFile(filePath, 'utf-8');
    return JSON.parse(content);
}

export { CONFIG, Logger, loadConfig };
