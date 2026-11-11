# The Impossible Editor

A new, user-friendly, and well-optimized [The Impossible Game](https://store.steampowered.com/app/251630/The_Impossible_Game/) level editor with additional features and improvements that the original editor doesn't have.

<img src="https://github.com/user-attachments/assets/b4862fec-3e66-4e72-a6b9-23b892e06a0d" />

[A short video trailer](https://www.youtube.com/watch?v=gHAwdB8RFJQ)

## Quick start
Just [download](https://github.com/Omegaplexx/TIGEditor/releases) and launch. No installation needed, it works right out of the box on Windows 8 and higher.

If you're on Windows 7, Vista or XP, [install .NET 4.0](https://www.microsoft.com/en-us/download/details.aspx?id=17718) first.

The program consists of a single portable `.exe` file less than 1 MB and supports all Windows versions starting with Windows XP Service Pack 3. It doesn't use the registry and stores all settings in an `.ini` file next to the editor itself.

## Features

### Upside down gravity

Use the Gravity tool to change the level's gravity at any time.

<img width="33%" src="https://github.com/user-attachments/assets/5460fb58-a8b9-472c-8a0d-15498168f15c" />

### Obstacle effects

Configure how obstacles disappear behind the player and appear in front of them (like in Heaven).

<img width="30%" src="https://github.com/user-attachments/assets/54a4493c-5a98-45f5-b80e-ff3aa03747dc" />
<img width="30%" src="https://github.com/user-attachments/assets/176b0cf3-107e-43ec-bb86-db6a9eb571de" />
<img width="30%" src="https://github.com/user-attachments/assets/13f0feae-6f5d-4bee-8960-6f6424cfa31e" />

<img width="30%" src="https://github.com/user-attachments/assets/17be08af-b2da-4076-9f23-f2ae9ec24b66" />
<img width="30%" src="https://github.com/user-attachments/assets/0b314c47-8457-4e7f-ac78-5b4b13b2560a" />
<img width="30%" src="https://github.com/user-attachments/assets/51234572-56b4-490e-afee-396e8506b8d3" />

Place the orange and blue effect lines. Orange is for sinking obstacles behind the player; blue is for rising ahead.

**Please note** that due to the game's bugginess, obstacle effects don't work as obviously as they might seem at first. For a more detailed explanation, right-click the Effects tool in the editor and select "More".

### Custom resources support

Add the modified `game_1.png` to your level folder, and the editor will use the graphics from that file. Use F2 to quickly switch between the built-in and custom graphics.

<img width="60%" src="https://github.com/user-attachments/assets/289ce3ba-fe65-47ec-90bb-205afcbc11ca" />

### Advanced building

Select, copy, and move objects with your mouse and keyboard!

<img width="60%" src="https://github.com/user-attachments/assets/453aed75-bd28-43de-a4ce-95209aa031fa" />

- Hold `Ctrl` to enter selection mode or to move already selected objects
- Hold `Ctrl` and `Alt` to copy selected objects
- Hold `Ctrl` and `Shift` to keep the selection (it won't be reset when you click the mouse again)
- Hold `Shift` to enter swipe mode

You can also move objects with arrow keys.

### A responsive music player

Play and scrub the soundtrack without any delays, set start positions, autoscroll the level during playback.

<img width="60%" src="https://github.com/user-attachments/assets/475627a9-1e08-41aa-8afe-f511972b1872" />

Features from left to right:
- Toggle camera following the playback line
- Play/pause
- Place/remove the start position
- Stop playback and remove the start position

Accessible via mouse and keyboard.

### New jump hint

Press `Alt + LMB` (left mouse button) to display jump guide. Don't confuse this with `Ctrl + LMB`—that's in the original editor!

<img width="60%" src="https://github.com/user-attachments/assets/2d95d6c1-f9f2-440c-a73a-798603b36dfd" />

Press `Alt + RMB` (right mouse button) to hide jump guide.


## Game patches

The editor includes several fixes and QoL features for The Impossible Game by applying reversible byte patches to the game's executable file.

### NoClip

This patch adds toggleable in-game invincibility mode via the F1 hotkey.

<img width="60%" src="https://github.com/user-attachments/assets/df5730a1-7ae7-498b-8f35-2b87b970e15f" />


### Custom resources and physics

Adds native support for custom graphics and physics for user-created levels.

<img width="60%" src="https://github.com/user-attachments/assets/91a98b11-5d22-409e-a6d2-062f1c245010" /> <img width="60%" src="https://github.com/user-attachments/assets/9022efa8-2359-4334-8b7c-e4ba1a50c147" />

### DPI fix

Prevents the game window from being stretched if the Windows interface scale is higher than 100%.

<img width="40%" src="https://github.com/user-attachments/assets/adc23ffc-91d9-4c39-acbf-a60bc3307514" /> <img width="40%" src="https://github.com/user-attachments/assets/fa27c524-21a2-4aa4-9d39-19e867919f50" />

### Hide "Medal Unlocked"

As the title says, it hides the annoying pop-up about unlocking a medal when a level starts. Pop-ups about actually getting medals are not affected.

<img width="60%" src="https://github.com/user-attachments/assets/9ef40358-04f9-4877-97c7-44947af824ac" />

### Jump to level folder

Opens the levels directory right away so you don't have to manually navigate to that folder every time.

<img width="40%" src="https://github.com/user-attachments/assets/76bc7842-b85a-4168-8b6f-db7c46efdd92" />
<img width="40%" src="https://github.com/user-attachments/assets/fad876ad-fb7d-48e8-9e4d-9d4f5367ae96" />

### Sync fix

Music sync correction between the first and subsequent attempts. At the start of the first attempt, the patch delays the start of the level by 150 milliseconds.

## Lanuages

The program supports only Russian and English. You can easily add your own language by placing your language's XML file in the `lang` folder inside the project folder. Compile the program using `build.bat`, and that's it! You can also send your translation to me, and I'll be happy to add it.
