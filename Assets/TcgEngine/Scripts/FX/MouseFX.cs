using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.FX
{
    /// <summary>
    /// FX that follows the mouse
    /// </summary>

    public class MouseFX : MonoBehaviour
    {
        public float speed = 20f;

        private Camera cam;                                        //缓存：原来每帧 Camera.main（内部是标签查找）
        private Plane plane = new Plane(Vector3.forward, 0f);      //平面固定，不必每帧 new

        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            if (cam == null)
                cam = Camera.main;
            if (cam == null)
                return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            plane.Raycast(ray, out float dist);
            Vector3 tpos = ray.GetPoint(dist);
            transform.position = Vector3.Lerp(transform.position, tpos, speed * Time.deltaTime);
        }
    }
}
