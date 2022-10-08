using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Assimp;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;


/*TODO: ADD CUBEMAPS lookat LEARNOPENGL.com*/

namespace OpenGLPathtracer
{
    using Vec3 = Silk.NET.Maths.Vector3D;

    using Vec3f = Vector3D<float>;
    using Vec2f = Vector2D<float>;
    using Vec4f = Vector4D<float>;
    using Quat = Quaternion<float>;

    using Mat4f = Silk.NET.Maths.Matrix4X4<float>;

    class Program
    {
        const float Deg2Rad = (float)Math.PI / 180.0f;

        static IWindow window;
        static GL Gl;

        static uint Vbo;
        static uint Ebo;
        static uint Vao;
        static uint shaderId;

        static string VertexShaderSource = @"
        #version 330 core
        in vec2 vPos;
        in vec2 vTexCoords;

        out vec2 uv;
        
        void main()
        {
            gl_Position = vec4(vPos.xy, 0, 1.0f);
            uv = vTexCoords;
        }
        ";

        static string FragmentShaderSource = @"
        #version 330 core
        in vec2 uv;

        out vec4 FragColor;
        uniform samplerCube skyhdri;
        uniform sampler2D tex1;
        uniform float iTime;

        vec3 tone(vec3 color, float gamma) //Reinhard based tone mapping
        {
	        float white = 2.;
	        float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	        float toneMappedLuma = luma * (1. + luma / (white*white)) / (1. + luma);
	        color *= toneMappedLuma / luma;
	        color = pow(color, vec3(1. / gamma));
	        return color;
        }

        float rand(float co) { return fract(sin(co*(91.3458)) * 47453.5453); } 
        float rand(vec2 co){ return fract(sin(dot(co.xy ,vec2(12.9898,78.233))) * 43758.5453); }

        #define PI 3.1415926538f

        //Primitives
        bool trace_sphere (vec3 ro, vec3 rd, vec3 o, float r, float tmin, float tmax, 
                          out vec3 p, out vec3 n, out vec3 t, out vec2 uv, out float dist)
        {
	        vec3 oc = ro - o;
            float a = dot (rd, rd);
            float b = dot (oc, rd);
            float c = dot (oc, oc) - r * r;
            float t0 = b * b - a * c;
            dist = tmax;

            if (t0 > 0.0)
            {
                float t1 = (-b - sqrt (t0)) / a;

                if (t1 < tmax && t1 > tmin)
                {
                    dist = t1;
                    p = ro + rd * dist;
                    n = (p - o) * (1.0f / r);
                    t = cross(vec3 (0, 1, 0), n);

                    uv.x = (1.f + atan (n.z, n.x) / PI) * 0.5f;
                    uv.y = acos (n.y) / PI;

                    return true;
                }

                t1 = (-b + sqrt (t0)) / a;

                if (t1 < tmax && t1 > tmin)
                {
                    dist = t1;
                    p = ro + rd * dist;
                    n = (p - o) * (1.0f / r);
                    t = cross(vec3 (0, 1, 0), n);

                    uv.x = (1.f + atan (n.z, n.x) /PI) * 0.5f;
                    uv.y = acos (n.y) / PI;

                    return true;
                }
            }

            return false;   
        }

        //BSDFs/PDFs
        vec3 sample_sphere (vec2 screen_uv) //Uniform sphere sample 
        {
            float cosPhi = 2.0 * rand (screen_uv*iTime) - 1.0;
            float sinPhi = sqrt (1.0 - cosPhi * cosPhi);
            float theta = 2.0 * PI * rand (rand (screen_uv*screen_uv * iTime));

            return vec3 (sinPhi * sin (theta),
                cosPhi,
                sinPhi * cos (theta));
        }

        vec3 sample_metal(vec3 n, vec3 rd, float fuzzy, vec2 screen_uv) //Mix (interpolate) reflection with diffuse by fuzz param basically
        {
            vec3 reflected = normalize(rd - n * dot(n, rd) * 2.0f);      
            return normalize(reflected + sample_sphere(screen_uv) * fuzzy);
        }

        vec3 sample_cosine_weighted(vec3 n, vec2 screen_uv) //cosine weighted hemisphere sampling
        {
            float phi = 2.f * PI * rand (screen_uv);
            float r2 = rand(rand (screen_uv*iTime));
            float r2s = sqrt (r2);

            vec3 w = normalize (n);
            vec3 u = normalize(cross((abs (w.x) > .1 ? vec3 (0, 1, 0) : vec3 (1, 0, 0)), w));
            vec3 v = cross (w, u);

            return normalize(u * cos (phi) * r2s + v * sin (phi) * r2s + w * sqrt (1.f - r2));
        }

        vec3 sample_phong_metal(vec3 n, vec3 rd, float e, vec2 screen_uv) //phong for metals
        {
            float phi = 2.f * PI * rand(screen_uv);
            float r2 = rand(rand(screen_uv));
  
            float cos_theta = pow(1.f - r2, 1.f / (e + 1.f));
            float sin_theta = sqrt(1.f - cos_theta * cos_theta);
    
            vec3 w = normalize((rd - n * dot(n, rd) * 2.0f));
            vec3 u = normalize(cross((abs(w.x) > 1.f ? vec3(0,1,0) : vec3(1,0,0)),w));
            vec3 v = cross(w, u);
    
            return normalize(u * cos(phi) * sin_theta + v * sin(phi) * sin_theta + w * cos_theta);
        }
        float F_Schlick (float cosine, float ref_idx) //schlick fresnel factor
        {
            float r0 = (1.0f - ref_idx) / (1.0f + ref_idx);
            r0 = r0 * r0;
            return r0 + (1.0f - r0) * pow (1.0f - cosine, 5.0f);
        }

        bool do_refract(vec3 v, vec3 n, float ni_over_nt, out vec3 refr)
        {
            vec3 uv = normalize(v);
            float dt = dot(uv, n);
            float t = 1.0f - ni_over_nt * ni_over_nt * (1.0f - dt * dt);
    
            if(t > 0.0f)
            {
                refr = (uv - n * dt) * ni_over_nt - n * sqrt(t);
     	        return true;   
            }
    
            return false;
        }
        vec3 sample_dielectric(vec3 n, vec3 rd, float ior, vec2 screen_uv)
        {
            float idotn = dot (rd, n);
    
            vec3 outward_normal;
            float ni_over_nt;
            float cosine;
            if (idotn > 0.0f) //from inside to outside or the other way around (dot(raydir, normal))
            {
                outward_normal = -n;
                ni_over_nt = ior;
                cosine = idotn / length(rd);
                cosine = sqrt (1.0f - ior * ior * (1.0f - cosine * cosine));
            }
            else 
            {
                outward_normal = n;
                ni_over_nt = 1.0f / ior;
                cosine = -idotn / length(rd);
            }

            vec3 refracted;
            float p;
            if (do_refract (rd, outward_normal, ni_over_nt, refracted)) //compute refr. dir and will it refract or reflect
                p = F_Schlick (cosine, ior); //probability of reflection is fresnel-schlick
            else
                p = 1.0f; //reflect 100% 

            if (rand (screen_uv * iTime) < p)
                return normalize(normalize((rd - n * dot (n, rd) * 2.0))); //reflection
            else
                return normalize(refracted); //refraction
        }
        
        struct Object
        {
            vec3 pos;
            float radius;
            int bsdf;
            vec3 diffuse;
            vec3 emissive;
            float arg; //ior or smoothness
        };

        const int bsdf_light = 0;
        const int bsdf_sphere = 1;
        const int bsdf_cosine_weighted = 2;
        const int bsdf_metal = 3;
        const int bsdf_phong_metal = 4;
        const int bsdf_dielectric = 5;

        //Main 
        bool trace_scene(inout vec3 ro, inout vec3 rd, out vec3 d, out vec3 e, vec2 screen_uv)
        { 


            //n: normal, p: point, t: tangent, d: diffuse, e: emission, ro: ray origin, rd: ray direction, uv: tex coords
            vec3 n, p, t; 
            vec2 uv;
            float dist;
            //Here in the event of a ray-object collision we set diffuse and emission based on 
            //e objects properties and set the direction of the ray based on the objects bsdf.
            //As well as tmax to the objects distance (doesnt make a difference yet since we lack depth sorting)
        
            vec3 p1 = vec3(sin(iTime * 0.8f) - 0.8f - 1,
                                    0.5f + sin(iTime * 2.f) * 0.5f,
                                    -5.f+sin(iTime * 1.5f));

            vec3 p2 = vec3(sin(iTime * 0.4f) - 0.8f,0.5f + sin(iTime * 2.f) * 0.5f,-10.f+sin(iTime * 1.5f));

            vec3 p3 = vec3(1.5f,0,-12.f + (sin((0.65f * PI) + iTime * 1.2f) * 1.5f));

            vec3 p4 = vec3(-1.5f + sin(iTime*3.0f)*0.6f,1.f + sin(iTime*3.f)*0.6f,-12);

            vec3 p5 = vec3(-3.f + sin(iTime * 0.5f) * 0.5f,0,-12.f + sin(iTime * 0.5f) * .3f);

            Object object[8];
            //object[1] = Object(p1, 1.f, bsdf_dielectric, vec3(0.9), vec3(0), 1.02f);
            object[1] = Object(p2, 1.f, bsdf_dielectric, vec3(0.9), vec3(0), 1.52);
            object[2] = Object(p3, 1.f, bsdf_metal, vec3(0.7f,0.7f,0), vec3(0), 0.7f);

            object[3] = Object(p4, 1.f, bsdf_light, vec3(0), vec3(1,1,0.4f)*30.0f, 0);
           // object[4] = Object(p5, 1.f, bsdf_metal, texture(tex1, uv).rgb, vec3(0), 0);
           // object[5] = Object(vec3(3.5f,0,-12.f), 1.f, bsdf_cosine_weighted, texture(tex1, uv).rgb, vec3(0), 0);

            //floor sphere
            object[0] = Object(vec3(-1.5f,-1005,-12), 1004.f, bsdf_cosine_weighted, vec3(.5,.2,.2), vec3(0), 0);

            for (int n = 7; n >= 0 ; --n) {
             for (int i = 0; i < n; ++i) {
                float distA = distance(object[i].pos, ro);
                float distB = distance(object[i+1].pos, ro);

                if(distA > distB)
               {
                   Object tmp = object[i+1];
                    object[i+1] = object[i];
                    object[i] = tmp;
                }    
              }
            }

            float tmin = 0.01f;
            float tmax = 1000.0f;
            bool hit = false;
            for(int i = 0; i < 4; i++)
            {
                Object v = object[i];
                if(trace_sphere(ro, rd, v.pos, v.radius, tmin, tmax, p, n, t, uv, dist))
                {
                   // if(dist < tmax)
                   //     tmax = dist;

                    d = v.diffuse;
                    e = v.emissive;
        
                    ro = p;

                    if(v.bsdf == bsdf_light)
                    {
                        rd = vec3(0);
                    }
                    else if(v.bsdf == bsdf_sphere)
                    {
                        rd = sample_sphere(screen_uv);
                    }
                    else if(v.bsdf == bsdf_cosine_weighted)
                    {
                        rd = sample_cosine_weighted(n, screen_uv);
                    }
                    else if(v.bsdf == bsdf_metal)
                    {
                        rd = sample_metal(n, rd, v.arg, screen_uv);
                    }
                    else if(v.bsdf == bsdf_phong_metal)
                    {
                        rd = sample_phong_metal(n, rd, v.arg, screen_uv);
                    }
                    else if(v.bsdf == bsdf_dielectric)
                    {
                        rd = sample_dielectric(n, rd, v.arg, screen_uv);
                    }
                    return true;
                }
            }

            return hit;
        }

        uniform vec2 iResolution;
        uniform int spp;
        uniform int minBounces;
        uniform int maxBounces;
        uniform float vFov; //30
        uniform vec3 pos; //-0.7,0,0
        uniform float focalLength; //10
        uniform float aperture;//0.1
        uniform vec3 up, right, fwd; //0,1,0 1,0,0 0,0,1  

        vec3 radiance(in vec3 ro, in vec3 rd, vec2 uv)
        {
            vec3 att = vec3(1);
            vec3 col;
    
            int i = 0;
            for(i = 0; i < maxBounces; i++) 
            {
                vec3 d, e;
        
                if(!trace_scene(ro, rd, d, e, uv)) 
                {
                    vec4 hdri = texture(skyhdri, rd);
                    col += att * hdri.rgb * hdri.a * 5;
                    break;
                }
        
                col += att * e;    //Emission  
                att *= d;         //Diffuse color
        
                if(i > minBounces) //Roulette sampling
                { 
                    float p = max(att.x, max(att.y, att.z));
                    if(rand(uv) > p)
                        break;
            
                    att /= p;
                }
            }
    
            return col / spp;
        }

        void main()
        {
            float aspect = iResolution.x/iResolution.y; //Perspective calculations (frustum)
            float hh = tan((vFov * (PI / 180.0f)) / 2.0f);
            float hw = aspect * hh;
            vec3 ll = pos - right * hw * focalLength - up * hh * focalLength - fwd * focalLength; 
            vec3 h = right * focalLength * 2.0f * hw; 
            vec3 v = up * focalLength * 2.0f * hh;
    
            vec3 color = vec3(0,0,0); 
            for(int i = 0; i < spp; i++) //Supersampling
            {
                vec2 uv_o = vec2(rand(gl_FragCoord.xy * float(i)), rand(rand(gl_FragCoord.xy)* float(i))); //Random offset
                vec2 uv = (gl_FragCoord.xy + uv_o)/iResolution.xy; //Normalized screen coordinates with offset
        
                float r = sqrt(rand(iTime * uv));  //Disc sampling (DoF)
                float theta = rand(rand((iTime * uv))) * 2.0f * PI;
                vec3 ds = vec3(cos(theta), sin(theta), 0) * (aperture/2.0f);
                vec3 o = right * ds.x + up * ds.y; //DoF offset 
        
                vec3 ro = pos + o; //ray origin
                vec3 rd = ll + h * uv.x + v * uv.y - pos - o; //ray dir
        
                color += clamp(tone(radiance(ro, rd, uv), 1.0), 0.0, 1.0);
            }
    
            FragColor = vec4(color / float(spp), 1); // Final color is average of samples tonemapped
        }
        ";

        static void Main(string[] args)
        {
            AssimpContext importer = new AssimpContext();

            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(800, 600);
            options.Title = "Potato RPG";

            window = Window.Create(options);
            window.Load += OnLoad;
            window.Update += OnUpdate;
            window.Render += OnRender;
            window.VSync = false;

            window.Run();
        }

        static uint skyboxTex, tex1;
        static int[] indices;

        static unsafe void OnLoad()
        {
            IInputContext input = window.CreateInput();
            foreach (var keyboard in input.Keyboards)
            {
                keyboard.KeyDown += KeyDown;
                keyboard.KeyUp += KeyUp;
            }

            foreach (var mice in input.Mice)
            {
                mice.Cursor.CursorMode = CursorMode.Raw;
                mice.MouseMove += OnMouseMove;
            }

            Gl = GL.GetApi(window);
            Gl.Enable(GLEnum.Texture2D);

            skyboxTex = Utils.LoadImageCubemap(Gl, 0, "./Res/nx.png", "./Res/px.png", "./Res/ny.png", "./Res/py.png", "./Res/nz.png", "./Res/pz.png");
            tex1 = Utils.LoadImage(Gl, 0, "./Res/soil.tif");

            Vao = Gl.GenVertexArray();
            Gl.BindVertexArray(Vao);

            Vbo = Gl.GenBuffer();

            float[] vertices = new float[]
            {
                -1, -1,  0,0,
                -1,  1,  0,1,
                 1, -1,  1,0,
                 1,  1,  1,1
            };

            indices = new int[]
            {
                2,1,0,
                1,2,3
            };


            Gl.BindBuffer(BufferTargetARB.ArrayBuffer, Vbo);
            fixed (void* v = &vertices[0])
            {
                Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }

            Ebo = Gl.GenBuffer();
            Gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, Ebo);
            fixed (void* i = &indices[0])
            {
                Gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), i, BufferUsageARB.StaticDraw); 
            }

            uint vertexShader = Gl.CreateShader(ShaderType.VertexShader);
            Gl.ShaderSource(vertexShader, VertexShaderSource);
            Gl.CompileShader(vertexShader);

            string infoLog = Gl.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(infoLog))
            {
                Console.WriteLine($"Error compiling vertex shader {infoLog}");
            }

            uint fragmentShader = Gl.CreateShader(ShaderType.FragmentShader);
            Gl.ShaderSource(fragmentShader, FragmentShaderSource);
            Gl.CompileShader(fragmentShader);

            infoLog = Gl.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(infoLog))
            {
                Console.WriteLine($"Error compiling fragment shader {infoLog}");
            }

            shaderId = Gl.CreateProgram();
            Gl.AttachShader(shaderId, vertexShader);
            Gl.AttachShader(shaderId, fragmentShader);
            Gl.LinkProgram(shaderId);

            Gl.GetProgram(shaderId, GLEnum.LinkStatus, out var status);
            if (status == 0)
                Console.WriteLine($"Error linking shader {Gl.GetProgramInfoLog(shaderId)}");

            Gl.DetachShader(shaderId, vertexShader);
            Gl.DetachShader(shaderId, fragmentShader);
            Gl.DeleteShader(vertexShader);
            Gl.DeleteShader(fragmentShader);

            Gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0); //pos
            Gl.EnableVertexAttribArray(0);

            Gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(4 * sizeof(float))); //uv
            Gl.EnableVertexAttribArray(1);
        }

        static float t = 0;

        static float deltaTime;
        static DateTime lastTime;

        static Quat rotation = Quat.Identity;
        const float DEG2RAD = MathF.PI / 180.0f;

        static unsafe void OnRender(double obj)
        {
            var dt = DateTime.Now - lastTime;
            lastTime = DateTime.Now;
            deltaTime = (float)dt.TotalSeconds;

            Gl.ClearColor(1,1,1,1);
            Gl.Clear((uint)ClearBufferMask.ColorBufferBit); 

            Gl.BindVertexArray(Vao);
            Gl.UseProgram(shaderId);

            if (deltaTime < 3)
            {
                t += deltaTime * 1.0f;
            }

            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(TextureTarget.Texture2D, skyboxTex);

            Gl.ActiveTexture(GLEnum.Texture1);
            Gl.BindTexture(TextureTarget.Texture2D, tex1);

            rotation = rotation * Quat.CreateFromYawPitchRoll(0,0, moveInput.W * 45.0f * deltaTime * DEG2RAD);      

            var rotAssimp = new Assimp.Quaternion(rotation.W, rotation.X, rotation.Y, rotation.Z);
            var rightAssimp = Assimp.Quaternion.Rotate(new Assimp.Vector3D(1, 0, 0), rotAssimp);
            var upAssimp = Assimp.Quaternion.Rotate(new Assimp.Vector3D(0, 1, 0), rotAssimp);
            var fwdAssimp = Assimp.Quaternion.Rotate(new Assimp.Vector3D(0, 0, 1), rotAssimp);
            var right = new Vec3f(rightAssimp.X, rightAssimp.Y, rightAssimp.Z);
            var up = new Vec3f(upAssimp.X, upAssimp.Y, upAssimp.Z);
            var fwd = new Vec3f(fwdAssimp.X, fwdAssimp.Y, fwdAssimp.Z);

            var movement = moveInput * 5f * deltaTime;
            pos += right * movement.X + up * movement.Y + fwd * movement.Z;

            Utils.SetUniform(Gl, shaderId, "iTime", t);
            Utils.SetUniform(Gl, shaderId, "skyhdri", 0);
            Utils.SetUniform(Gl, shaderId, "tex1", 1);

            Utils.SetUniform(Gl, shaderId, "iResolution", new Vec2f(800, 600));
            Utils.SetUniform(Gl, shaderId, "spp", 20);
            Utils.SetUniform(Gl, shaderId, "minBounces", 5);
            Utils.SetUniform(Gl, shaderId, "maxBounces", 7);
            Utils.SetUniform(Gl, shaderId, "vFov", 30.0f);
            Utils.SetUniform(Gl, shaderId, "pos", pos);
            Utils.SetUniform(Gl, shaderId, "focalLength", 10.0f);
            Utils.SetUniform(Gl, shaderId, "aperture", 0.1f);

            Utils.SetUniform(Gl, shaderId, "right", right);
            Utils.SetUniform(Gl, shaderId, "up", up);
            Utils.SetUniform(Gl, shaderId, "fwd", fwd);


            Utils.SetUniform(Gl, shaderId, "skyhdri", 0);
            Utils.SetUniform(Gl, shaderId, "tex1", 1);

            Gl.DrawElements(GLEnum.Triangles, (uint)indices.Length, DrawElementsType.UnsignedInt, null);
        }

        static DateTime lastFPSReadout;
        static int fpsSamples;
        static float fpsSum;

        static void OnUpdate(double obj)
        {
            fpsSum += 1.0f / deltaTime;
            fpsSamples++;

            if ((DateTime.Now - lastFPSReadout).TotalSeconds > 0.5f)
            {
                var avgFps = fpsSum / fpsSamples;
                fpsSum = 0;
                fpsSamples = 0;

                lastFPSReadout = DateTime.Now;
                window.Title = $"PotatoRPG FPS: {avgFps}";
            }
        }

        static void OnClose()
        {
            Gl.DeleteBuffer(Vbo);
            Gl.DeleteBuffer(Ebo);
            Gl.DeleteVertexArray(Vao);
            Gl.DeleteProgram(shaderId);
            Gl.DeleteTexture(skyboxTex);
        }

        static Vec4f moveInput;
        static Vec3f pos = new Vec3f(-0.7f, 0, 0);

        private static void KeyDown(IKeyboard arg1, Key arg2, int arg3)
        {
            if (arg2 == Key.Escape)
                window.Close();

            if (arg2 == Key.W)
                moveInput.Z = -1;
            if (arg2 == Key.S)
                moveInput.Z = 1;

            if (arg2 == Key.A)
                moveInput.X = -1;
            if (arg2 == Key.D)
                moveInput.X = 1;

            if (arg2 == Key.ShiftLeft)
                moveInput.Y = -1;
            if (arg2 == Key.Space)
                moveInput.Y = 1;

            if (arg2 == Key.Q)
                moveInput.W = -1;
            if (arg2 == Key.E)
                moveInput.W = 1;
        }

        private static void KeyUp(IKeyboard arg1, Key arg2, int arg3)
        {
            if (arg2 == Key.W)
                moveInput.Z = 0;
            if (arg2 == Key.S)
                moveInput.Z = 0;

            if (arg2 == Key.A)
                moveInput.X = 0;
            if (arg2 == Key.D)
                moveInput.X = 0;

            if (arg2 == Key.ShiftLeft)
                moveInput.Y = 0;
            if (arg2 == Key.Space)
                moveInput.Y = 0;

            if (arg2 == Key.Q)
                moveInput.W = 0;
            if (arg2 == Key.E)
                moveInput.W = 0;
        }

        static System.Numerics.Vector2 prevMousePos;
        static bool prevSet = false;

        private static unsafe void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
        {
            const float mouseSensitivity = 0.15F;

            if (prevSet)
            {
                var delta = prevMousePos - position;

                rotation = rotation *Quat.CreateFromYawPitchRoll(
                        delta.X * mouseSensitivity * DEG2RAD, 
                        delta.Y * mouseSensitivity * DEG2RAD, 0);
            }
            else
                prevSet = true;

            prevMousePos = position;

        }
    }
}
