#include <stdio.h>
#include <string.h>

#define MAX_NAME 64

typedef struct {
    char name[MAX_NAME];
    int version;
    int debug;
} Config;

static void print_config(const Config *config) {
    printf("Config{%s v%d debug=%d}\n", config->name, config->version, config->debug);
}

int main(void) {
    Config config;
    strncpy(config.name, "Glance", MAX_NAME);
    config.version = 1;
    config.debug = 1;

    print_config(&config);
    return 0;
}
