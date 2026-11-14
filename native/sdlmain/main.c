/* Bridges SDL's Android entry point (SDL_main) into the managed OpenRA entry point. */
#include <stddef.h>
#include <android/log.h>

typedef int (*managed_main_fn)(int argc, char *argv[]);
static managed_main_fn g_managed_main = NULL;

__attribute__((visibility("default"))) void openra_set_main(managed_main_fn fn)
{
	g_managed_main = fn;
}

__attribute__((visibility("default"))) int SDL_main(int argc, char *argv[])
{
	if (g_managed_main == NULL)
	{
		__android_log_print(ANDROID_LOG_ERROR, "OpenRA", "Managed entry point was not registered");
		return 1;
	}

	return g_managed_main(argc, argv);
}
