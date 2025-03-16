using UnityEngine;
using System.Collections;

#if VR
using KK.RootMotion;
using KK.RootMotion.FinalIK;
#elif NOVR
using RootMotion;
using RootMotion.FinalIK;
#endif

namespace Koik.IKAmplifier
{

    /// <summary>
    /// Demo script that amplifies the motion of a body part relative to the root of the character or another body part.
    /// </summary>
    public class Amplifier : Koik.IKAmplifier.OffsetModifier
    {

        /// <summary>
        /// Body is amplifying the motion of "transform" relative to the "relativeTo".
        /// </summary>
        //[System.Serializable]
        public class Body
        {

            /// <summary>
            /// Linking this to an effector
            /// </summary>
            //[System.Serializable]
            public class EffectorLink(FullBodyBipedEffector effector, float weight)
            {
                [Tooltip("Type of the FBBIK effector to use")]
                public FullBodyBipedEffector effector = effector;
                [Tooltip("Weight of using this effector")]
                public float weight = weight;
            }

            [Tooltip("The Transform that's motion we are reading.")]
            public Transform transform;
            [Tooltip("Amplify the 'transform's' position relative to this Transform.")]
            public Transform relativeTo;
            [Tooltip("Linking the body to effectors. One Body can be used to offset more than one effector.")]
            public EffectorLink[] effectorLinks;
            //[Tooltip("Amplification magnitude along the up axis of the character.")]
            //public float verticalWeight = 1f;
            //[Tooltip("Amplification magnitude along the horizontal axes of the character.")]
            //public float horizontalWeight = 1f;
            [Tooltip("Speed of the amplifier. 0 means instant.")]
            public float speed = 3f;

            private Vector3 lastRelativePos;
            private Vector3 smoothDelta;
            private bool firstUpdate;

            private Vector3 GetRelativePos => relativeTo.InverseTransformDirection(transform.position - relativeTo.position);

            /// <summary>
            /// Update on animator's state change since we have non smooth states to avoid weird jumps.
            /// </summary>
            internal void UpdateRelativePos() => firstUpdate = true;

            private bool AnyWeight()
            {
                foreach (var effector in effectorLinks)
                {
                    if (effector.weight > 0f) return true;
                }
                return false;
            }
            // Update the Body
            public void Update(IKSolverFullBodyBiped solver, float w, float deltaTime)
            {
                if (transform == null || relativeTo == null || !AnyWeight()) return;

                var relativePos = GetRelativePos;

                // Initiating
                if (firstUpdate)
                {
                    lastRelativePos = relativePos;
                    //smoothDelta = Vector3.zero;
                    firstUpdate = false;
                }

                // Find how much the relative position has changed
                Vector3 delta = (relativePos - lastRelativePos) / deltaTime;

                // Smooth the change
                smoothDelta = speed <= 0f ? delta : Vector3.Lerp(smoothDelta, delta, deltaTime * speed);

                // Convert to world space
                Vector3 worldDelta = relativeTo.TransformDirection(smoothDelta);

                // Extract horizontal and vertical offset
                //Vector3 offset = V3Tools.ExtractVertical(worldDelta, solver.GetRoot().up, verticalWeight) + V3Tools.ExtractHorizontal(worldDelta, solver.GetRoot().up, horizontalWeight);

                // Apply the amplitude to the effector links
                foreach (var effectorLink in effectorLinks)
                {
                    solver.GetEffector(effectorLink.effector).positionOffset += effectorLink.weight * w * worldDelta;
                }
                //for (int i = 0; i < effectorLinks.Length; i++)
                //{
                //    //solver.GetEffector(effectorLinks[i].effector).positionOffset += offset * w * effectorLinks[i].weight;
                //    solver.GetEffector(effectorLinks[i].effector).positionOffset += effectorLinks[i].weight * w * worldDelta;
                //}

                lastRelativePos = relativePos;
            }

            // Multiply 2 vectors
            private static Vector3 Multiply(Vector3 v1, Vector3 v2)
            {
                v1.x *= v2.x;
                v1.y *= v2.y;
                v1.z *= v2.z;
                return v1;
            }
        }

        [Tooltip("The amplified bodies.")]
        public Body[] bodies;

        // Called by IKSolverFullBody before updating
        protected override void OnModifyOffset()
        {
            if (!ik.fixTransforms)
            {
                if (!Warning.logged) Warning.Log("Amplifier needs the Fix Transforms option of the FBBIK to be set to true. Otherwise it might amplify to infinity, should the animator of the character stop because of culling.", transform);
                return;
            }

            foreach (var body in bodies)
            {
                body.Update(ik.solver, weight, Time.deltaTime);
            }
        }

        public void UpdatePositions() //(bool turnOn = false)
        {
            enabled = true;
            foreach (var body in bodies)
            {
                body.UpdateRelativePos();
            }
        }
    }
}
