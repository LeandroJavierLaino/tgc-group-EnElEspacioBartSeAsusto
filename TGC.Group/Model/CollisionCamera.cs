﻿using Microsoft.DirectX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TGC.Core.BoundingVolumes;
using TGC.Core.Collision;
using TGC.Core.Geometry;
using TGC.Core.Utils;

namespace TGC.Group.Model
{
    class CollisionCamera
    {
        private const float EPSILON = 0.05f;

        private bool GravityEnabled = true;
        private Vector3 GravityForce = new Vector3(0, -10, 0);

        private readonly List<TgcBoundingAxisAlignBox> objetosCandidatos = new List<TgcBoundingAxisAlignBox>();

        public CollisionCamera()
        {
            GravityEnabled = true;
            GravityForce = new Vector3(0, -10, 0);
            SlideFactor = 1.3f;
        }

        /// <summary>
        ///     Vector que representa la fuerza de gravedad.
        ///     Debe tener un valor negativo en Y para que la fuerza atraiga hacia el suelo
        /// </summary>
        //public Vector3 GravityForce { get; set; }

        /// <summary>
        ///     Habilita o deshabilita la aplicación de fuerza de gravedad
        /// </summary>
        //public bool GravityEnabled { get; set; }

        /// <summary>
        ///     Multiplicador de la fuerza de Sliding
        /// </summary>
        public float SlideFactor { get; set; }

        /// <summary>
        ///     Mover BoundingSphere con detección de colisiones, sliding y gravedad.
        ///     Se actualiza la posición del centrodel BoundingSphere.
        /// </summary>
        /// <param name="characterSphere">BoundingSphere del cuerpo a mover</param>
        /// <param name="movementVector">Movimiento a realizar</param>
        /// <param name="obstaculos">BoundingBox de obstáculos contra los cuales se puede colisionar</param>
        /// <returns>Desplazamiento relativo final efecutado al BoundingSphere</returns>
        public Vector3 moveCharacter(TgcBoundingSphere characterSphere, Vector3 movementVector,
            List<TgcBoundingAxisAlignBox> obstaculos)
        {
            var originalSphereCenter = characterSphere.Center;

            //Realizar movimiento
            collideWithWorld(characterSphere, movementVector, obstaculos);

            //Aplicar gravedad
            if (GravityEnabled)
            {
                collideWithWorld(characterSphere, GravityForce, obstaculos);
            }

            return characterSphere.Center - originalSphereCenter;
        }

        public void toggleGravity()
        {
            GravityEnabled = !GravityEnabled;
        }

        /// <summary>
        ///     Detección de colisiones, filtrando los obstaculos que se encuentran dentro del radio de movimiento
        /// </summary>
        private void collideWithWorld(TgcBoundingSphere characterSphere, Vector3 movementVector, List<TgcBoundingAxisAlignBox> obstaculos)
        {
            if (movementVector.LengthSq() < EPSILON)
            {
                return;
            }

            var lastCenterSafePosition = characterSphere.Center;

            //Dejar solo los obstáculos que están dentro del radio de movimiento de la esfera
            var halfMovementVec = Vector3.Multiply(movementVector, 0.5f);
            var testSphere = new TgcBoundingSphere( characterSphere.Center + halfMovementVec, halfMovementVec.Length() + characterSphere.Radius);

            objetosCandidatos.Clear();

            foreach (var obstaculo in obstaculos)
            {
                if (TgcCollisionUtils.testSphereAABB(testSphere, obstaculo))
                {
                    objetosCandidatos.Add(obstaculo);
                }
            }

            //Detectar colisiones y deplazar con sliding
            doCollideWithWorld(characterSphere, movementVector, objetosCandidatos, 0);

            //Manejo de error. No deberiamos colisionar con nadie si todo salio bien
            foreach (var obstaculo in objetosCandidatos)
            {
                if (TgcCollisionUtils.testSphereAABB(characterSphere, obstaculo))
                {
                    //Hubo un error, volver a la posición original
                    characterSphere.setCenter(lastCenterSafePosition);
                    return;
                }
            }
        }

        /// <summary>
        ///     Detección de colisiones recursiva
        /// </summary>
        public void doCollideWithWorld(TgcBoundingSphere characterSphere, Vector3 movementVector,List<TgcBoundingAxisAlignBox> obstaculos, int recursionDepth)
        {
            //Limitar recursividad
            if (recursionDepth > 5)
            {
                return;
            }

            //Ver si la distancia a recorrer es para tener en cuenta
            var distanceToTravelSq = movementVector.LengthSq();

            if (distanceToTravelSq < EPSILON)
            {
                return;
            }

            //Posicion deseada
            var originalSphereCenter = characterSphere.Center;
            var nextSphereCenter = originalSphereCenter + movementVector;

            //Buscar el punto de colision mas cercano de todos los objetos candidatos
            var minCollisionDistSq = float.MaxValue;
            var realMovementVector = movementVector;
            TgcBoundingAxisAlignBox.Face collisionFace = null;
            TgcBoundingAxisAlignBox collisionObstacle = null;
            var nearestPolygonIntersectionPoint = Vector3.Empty;
            foreach (var obstaculoBB in obstaculos)
            {
                //Obtener los polígonos que conforman las 6 caras del BoundingBox
                var bbFaces = obstaculoBB.computeFaces();

                foreach (var bbFace in bbFaces)
                {
                    var pNormal = TgcCollisionUtils.getPlaneNormal(bbFace.Plane);

                    var movementRay = new TgcRay(originalSphereCenter, movementVector);
                    float brutePlaneDist;
                    Vector3 brutePlaneIntersectionPoint;
                    if (!TgcCollisionUtils.intersectRayPlane(movementRay, bbFace.Plane, out brutePlaneDist, out brutePlaneIntersectionPoint))
                    {
                        continue;
                    }

                    var movementRadiusLengthSq = Vector3.Multiply(movementVector, characterSphere.Radius).LengthSq();
                    if (brutePlaneDist * brutePlaneDist > movementRadiusLengthSq)
                    {
                        continue;
                    }

                    //Obtener punto de colisión en el plano, según la normal del plano
                    float pDist;
                    Vector3 planeIntersectionPoint;
                    Vector3 sphereIntersectionPoint;
                    var planeNormalRay = new TgcRay(originalSphereCenter, -pNormal);
                    var embebbed = false;
                    var collisionFound = false;
                    if (TgcCollisionUtils.intersectRayPlane(planeNormalRay, bbFace.Plane, out pDist, out planeIntersectionPoint))
                    {
                        //Ver si el plano está embebido en la esfera
                        if (isEmbebbed( pDist, characterSphere.Radius))
                        {
                            embebbed = true;
                            
                            //TODO: REVISAR ESTO, caso embebido a analizar con más detalle
                            sphereIntersectionPoint = originalSphereCenter - pNormal * characterSphere.Radius;
                        }
                        //Esta fuera de la esfera
                        else
                        {
                            //Obtener punto de colisión del contorno de la esfera según la normal del plano
                            sphereIntersectionPoint = originalSphereCenter - Vector3.Multiply(pNormal, characterSphere.Radius);

                            //Disparar un rayo desde el contorno de la esfera hacia el plano, con el vector de movimiento
                            var sphereMovementRay = new TgcRay(sphereIntersectionPoint, movementVector);
                            if (!TgcCollisionUtils.intersectRayPlane(sphereMovementRay, bbFace.Plane, out pDist, out planeIntersectionPoint))
                            {
                                //no hay colisión
                                continue;
                            }
                        }

                        //Ver si planeIntersectionPoint pertenece al polígono
                        Vector3 newMovementVector;
                        float newMoveDistSq;
                        Vector3 polygonIntersectionPoint;
                        if (pointInBounbingBoxFace(planeIntersectionPoint, bbFace))
                        {
                            if (embebbed)
                            {
                                //TODO: REVISAR ESTO, nunca debería pasar
                                //throw new Exception("El polígono está dentro de la esfera");
                                characterSphere.moveCenter(new Vector3(originalSphereCenter.X, 0, originalSphereCenter.Z)  - new Vector3( pNormal.X,0, pNormal.Z) * characterSphere.Radius);
                            }

                            polygonIntersectionPoint = planeIntersectionPoint;
                            collisionFound = true;
                        }
                        else
                        {
                            //Buscar el punto mas cercano planeIntersectionPoint que tiene el polígono real de esta cara
                            polygonIntersectionPoint = TgcCollisionUtils.closestPointRectangle3d(planeIntersectionPoint, bbFace.Extremes[0], bbFace.Extremes[1], bbFace.Extremes[2]);

                            //Revertir el vector de velocidad desde el nuevo polygonIntersectionPoint para ver donde colisiona la esfera, si es que llega
                            var reversePointSeg = polygonIntersectionPoint - movementVector;
                            if (TgcCollisionUtils.intersectSegmentSphere(polygonIntersectionPoint, reversePointSeg,
                                characterSphere, out pDist, out sphereIntersectionPoint))
                            {
                                collisionFound = true;
                            }
                        }

                        if (collisionFound)
                        {
                            //Nuevo vector de movimiento acotado
                            newMovementVector = polygonIntersectionPoint - sphereIntersectionPoint;
                            newMoveDistSq = newMovementVector.LengthSq();

                            if (newMoveDistSq <= distanceToTravelSq && newMoveDistSq < minCollisionDistSq)
                            {
                                minCollisionDistSq = newMoveDistSq;
                                realMovementVector = newMovementVector;
                                nearestPolygonIntersectionPoint = polygonIntersectionPoint;
                                collisionFace = bbFace;
                                collisionObstacle = obstaculoBB;
                            }
                        }
                    }
                }
            }

            //Si nunca hubo colisión, avanzar todo lo requerido
            if (collisionFace == null)
            {
                //Avanzar hasta muy cerca
                var movementLength = movementVector.Length();
                movementVector.Multiply((movementLength - EPSILON) / movementLength);
                characterSphere.moveCenter(movementVector);
                return;
            }

            //Solo movernos si ya no estamos muy cerca
            if (minCollisionDistSq >= EPSILON)
            {
                //Mover el BoundingSphere hasta casi la nueva posición real
                var movementLength = realMovementVector.Length();
                realMovementVector.Multiply((movementLength - EPSILON) / movementLength);
                characterSphere.moveCenter(realMovementVector);
            }

            //Calcular plano de Sliding
            var slidePlaneOrigin = nearestPolygonIntersectionPoint;
            var slidePlaneNormal = characterSphere.Center - nearestPolygonIntersectionPoint;
            slidePlaneNormal.Normalize();

            var slidePlane = Plane.FromPointNormal(slidePlaneOrigin, slidePlaneNormal);

            //Proyectamos el punto original de destino en el plano de sliding
            var slideRay = new TgcRay(nearestPolygonIntersectionPoint + Vector3.Multiply(movementVector, SlideFactor), slidePlaneNormal);
            float slideT;
            Vector3 slideDestinationPoint;

            if (TgcCollisionUtils.intersectRayPlane(slideRay, slidePlane, out slideT, out slideDestinationPoint))
            {
                //Nuevo vector de movimiento
                var slideMovementVector = slideDestinationPoint - nearestPolygonIntersectionPoint;

                if (slideMovementVector.LengthSq() < EPSILON)
                {
                    return;
                }

                //Recursividad para aplicar sliding
                doCollideWithWorld(characterSphere, slideMovementVector, obstaculos, recursionDepth + 1);
            }
        }

        /// <summary>
        ///     Ver si un punto pertenece a una cara de un BoundingBox
        /// </summary>
        /// <returns>True si pertenece</returns>
        private bool pointInBounbingBoxFace(Vector3 p, TgcBoundingAxisAlignBox.Face bbFace)
        {
            var min = bbFace.Extremes[0];
            var max = bbFace.Extremes[3];

            return p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;
        }

        /// <summary>
        /// Evalua si el bounding sphere quedo embebido en el poligono
        /// </summary>
        /// <param name="pDist"></param>
        /// <param name="radius"></param>
        /// <returns>True si quedo embebido</returns>
        public bool isEmbebbed(float pDist, float radius)
        {
            return pDist <= radius;
        }
    }
}

