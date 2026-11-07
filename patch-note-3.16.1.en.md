📣 Shabpa 3.16.1 is out

🛠 Fixes
• 🪟 The overlay no longer locks the whole app: it used to wait for the graphics driver while finding the monitor, and the window sometimes stopped for up to 30 seconds
• ⏱ Closing the app and changing the language no longer wait 3 to 5 seconds
• 🏅 The Medals page opens about 4 times faster (from half a second to a tenth of a second)

💡 Lighting
• Every device now has its own separate road to the lighting server; a slow device (such as memory or the motherboard) no longer holds back fans and LED strips
• Each frame is drawn for the moment it really reaches the device, so devices change closer together
• Memory and other devices the lighting server cannot see are still not controlled (to be looked into in the next version)

🔎 For future freezes
• The freeze log now says exactly what held the window, and how long each page takes to open is recorded; if it freezes again, send the log so we can fix it
