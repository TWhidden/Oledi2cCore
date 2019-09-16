# Oled I2C .net Core + FT232H USB I2C

This project takes bits of the internet and makes it work for the project I am working on. 

Porting C++ code to C#, figuring out how to work with these different OLED displays.

Currently working with an FT232H (https://www.adafruit.com/product/2264) Device so I can remotely control a screen. When this project is complete, it will support both *nix and Windows.

Currently targeting .netcore3 (im sure it will work with previous core/net versions). 

Current Display I am using is an SH1106 (https://www.amazon.com/gp/product/B01MRR4LVE) - Note, this display, probably cost me 20 hours of my life with the odd-ball extra stuff you have to do with it due to it actually having a memory buffer 132x64 but only displaying 128x64.  If you want less of a headache, get an SSD1306 - I will ensure this works with that one also. 

This code is up for some major refactoring. 

The FtdiI2cCore project is the abstraction over the I2C / USB FT232H. This can be used to talk to any I2C with this chip set and .net core. 

The OledI2cCore is a port from some Oled code I used previously with Node, and cut parts from C++ found around the web. 

I have a Test lib so you can test your ideas out, and make sure they are working. 

