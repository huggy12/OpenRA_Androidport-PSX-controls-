package net.openra.android;

import android.os.Bundle;
import android.view.WindowManager;

import org.libsdl.app.SDLActivity;

/**
 * Hosts the OpenRA engine inside SDL's Android activity.
 * SDL loads libmain.so and calls SDL_main on its own thread, which forwards into the managed
 * entry point registered by OpenRAApplication.
 */
public class OpenRAActivity extends SDLActivity {
    @Override
    protected String[] getLibraries() {
        return new String[] { "SDL2", "main" };
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }
}
