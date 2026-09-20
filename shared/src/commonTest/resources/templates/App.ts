export interface Config {
    name: string;
    version: number;
    debug: boolean;
}

export type Env = "dev" | "prod";

export function createConfig(name: string, version: number): Config {
    return { name, version, debug: false };
}

export class Registry {
    private items: Map<string, Config> = new Map();

    add(config: Config): void {
        this.items.set(config.name, config);
    }

    get(name: string): Config | undefined {
        return this.items.get(name);
    }
}
