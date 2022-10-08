# OpenGLPathTracer

Pathtracer which runs on GLSL shader written in C#

Code is adapted from my own GLSL from ShaderToy which itself
was adapted from my own simplified CUDA C++ code. 

Almost all the code is contained in a 700ln file. (the shadertoy GLSL was 352 loc)

Link https://www.shadertoy.com/view/wtfcDB


![render2](https://user-images.githubusercontent.com/8173214/193342359-8d2c40f9-334f-4641-99a4-4f537cb65be4.png)


Features:
- Sphere primitives
- BSDFs: Uniform diffuse, Phong Metal, Reflective metal, Cosine weighted, dielectric.
- Real depth of field effect
- Depth sorting (very dumb)
- Supersampling
- Tonemapping
- All in GLSL shader
- Camera WASD translation and mouse rotation using quaternions (6 axis)
- Real time rendering (60+ fps with 1spp, 1000+ fps with 1 spp no depth sorting dumb algo.)
- Cubemaps and texturing 

It works by going through every pixel and checking if a ray coming from that pixel intersects an object, if it does
we add up the accumulated attenuation and the emission of the object (if emissive like a light). We then multiply the attenuation by the color of the object.

When we hit an object, we can then shoot rays from the intersection points and do the same thing many times until we have hit a light or enough objects.

If we don't, then we can sample from the skybox (cubemap) according to ray direction xyz. 

We do this radiance algorithm (shooting rays from each pixel) several times for each pixel, then average the color. This is done in case the pixel never actually hits a light (since pixels are integers we get aliasing so we need anti-aliasing).

Afterwards we tonemap, that is, we map the colors to between black and white since they can add up to colors brighter than white. It also makes colors more realistic (using curve). (light strength is done by having light color multiplied by factor).

Depth of field blurring is done by offseting rays from their origin location (initial rays) in a disk pattern, same for perspective but according to frustum. 

Depth sorting is done to know which objects are in front of others, it's done by sorting objects by distance from ray origin. (super dumb since it's done every frame)

For quaternion rotations we have a quaternion and we multiply it by a newly created quaternion from euler rotations (yaw, pitch, roll separately) every time we rotate.
This quaternion is then multiplied by reference unit vector directions (right 1,0,0 up 0,1,0 fwd 0,0,1) to get new directions and those are sent to the shader to be able to orient the rays shot from each pixel.

For translation we just send a translation vec3 to the shader.

The vertex shader simply passes a bunch of 2D coordinates for a 2 triangle quad which is what everything is drawn onto.

All of the radiance and ray intersection stuff is done in the fragment shader, pixel by pixel. 

It's just a very naive pathtracer but whatever maybe serves as example.
