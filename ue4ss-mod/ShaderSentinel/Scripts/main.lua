-- Shader Sentinel: starts the external shader-compile overlay with the game.
-- The exe watches GPU driver shader-cache activity from outside the game
-- process and exits on its own once compilation is finished (or after a few
-- quiet minutes if no compilation was needed, or when the game closes).

-- Relative to the game's working directory (OblivionRemastered\Binaries\Win64)
local EXE = "ue4ss\\Mods\\ShaderSentinel\\OblivionShaderSentinel.exe"

local function launch()
    local f = io.open(EXE, "rb")
    if f == nil then
        print("[ShaderSentinel] exe not found: " .. EXE .. "\n")
        return
    end
    f:close()
    -- 'start' detaches, so the game's load thread is not blocked.
    os.execute('start "" "' .. EXE .. '"')
    print("[ShaderSentinel] overlay watcher launched\n")
end

if ExecuteAsync ~= nil then
    ExecuteAsync(launch)
else
    launch()
end
