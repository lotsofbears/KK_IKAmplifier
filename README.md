# KK_IKAmplifier
A BepInEx plugin for Koikatsu and Koikatsu Sunshine that gives an extra oomph to animations in main game when IK is enabled.

## Installation
1. Install dependencies: 
    - [BepInEx v5](https://github.com/BepInEx/BepInEx/releases)
    - [IllusionModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI)
2. Download the latest [release](https://github.com/lotsofbears/KK_IKAmplifier/releases)
3. Extract downloaded .zip file to your game folder (where the BepInEx folder and the game .exe are)

## Incompatibilities
* Animations with disabled IK.
  
## Overview
The plugin is meant for use with cross fader, either from [VRPlugin](https://github.com/IllusionMods/KKS_VR/) (works outside of vr too) or another plugin that can be found in different plugin suits for the game.
Without it looks weird at animations' state transitions.

Has two versions for [VRPlugin](https://github.com/IllusionMods/KKS_VR/) and flat screen, both may be present at the same time, only the appropriate one will load at runtime.

More robust in VRPlugin due to alternative IK setup being used. 

Comes with variety of settings in-game and .CSV file to adjust default weights or those for particular animations.
