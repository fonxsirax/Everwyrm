
BEFORE YOU START:
- you need Unity 6.3
- you need URP SRP pipline 17.3 if you use higher please import 17.3 support pack.
- wind setup is in wind prefab at each scene

Step 1 - You can improve FPS amount by 30% if you change rendering path from forward+ to deferred+ at PC Renderer setting
So if you need deferred Find File "PC_Renderer" and change Rendering path from forward+ to deferred+. Forward+ render is ok too but it's slower for big open scenes.

Step 2 - Setup Shadows and other render setups. Find File "PC_RPAsset" 
    - Change shadow distance to 300 or higer
	- Turn on "Opaque Texture" this will fix water translucency and distortion if its turned off
	- Turn on "Depth Texture" this will fix water visibility at playmode if its turned off
	- Optionaly use 1k or 2k shadow resolution. We used 2k.
	- Turn on HDR if its turned off

Step 3 Go to project settings: 
    - Player and set:  Color Space to Linear
    - Quality settings: Go to quality settings and: 
	     * turn turn off vsync
	     * lod bias should be around 1.5-2 and 1 for low end devices.
                        
Step 4 Find "URP Mountain Scene" and open it.

Step 5 - chose way of movment. Movie track or free movment.
	Chose camera and turn on or off "playable directior" and "animation" or leave free camera movment turned on.

Step 6 - HIT PLAY!:)

Step 7 -  Make note that unity often compile shaders even after you hit play for long time, so performance will rise up after unity end shader compilation
Wait a moment until it end. 
At urp and hd rp seams unity SRP batched batches are not counted as saved by batching so they can lead to misunderstanding.
At unity standard render scene got 1500 batches and rest is saved by batching. 
At hd and urp there is alot more batches in counter but unity don't show how much of them are batched by srp batcher.
In the past unity count srp batches into save by batching now it's not. We use srp batcher in hd and urp scenes so stats value can lead to misunderstanding. 

About scene construction:
		- There is post process profile: Manage post process by scene post process object.
		- Prefab wind manage wind speed and direction at the scene

