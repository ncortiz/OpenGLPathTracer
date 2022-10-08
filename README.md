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
