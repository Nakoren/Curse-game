using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;


public class Enemy_AI_Mod : MonoBehaviour
{
    //Создаваемые объекты
    public GameObject ammoProjectile;   //Шаблон пули
    public GameObject playerFantom;     //Шаблон фантома игрока

    //Настраиваемые переменные, отвечающие за исходные значения таймеров
    public float fireCooldown;  //Откат выстрела
    public float targetDelay;   //Задержка между актуализацией состояния ИИ
    public float trackTime;     //Таймер отслеживания игрока

   //Настраиваемые переменные, отвечающие за параметры алгоритма
    public float aimErrorMargin;                //Отклонение выстрела
    public int ricochetZoneRays;                //Количество лучей, выпускаемых в одну подходящую зону отражения
    public int ricochetZoneDetectionRays;       //Количество лучей, выпускаемых для поиска зон отражений
    public float ricochetZoneDistanceBetween;   //Максимальное расстояние между точками соприкосновения соседних лучей, при котором два луча считаются одной зоной отражения
    public float ricochetZoneAngleDifference;   //Максимальная разница углов соприкосновения между лучами и поверхностью соседних лучей, при котором два луча считаются одной зоной отражения
    public float ricochetZoneDistance;          //Максимальное расстояние между первой и последней точкой соприкосновения одной зоны отражения

    //Внутренние переменные класса
    private Vector3 targetLastPosition;     //Последниее видимое местоположения игрока
    private NavMeshAgent agent;             //Агент, отвечающий за передвижения бота
    private Transform playerTransform;      //Transform компонент игрока
    private Vector3 predictedPosition;      //Предсказываемое местоположения игрока
    private float currentCooldown;          //Текущий откат выстрела
    private float currentDelay;             //Текущая задержка проверки актуальности состояния
    private float trackTimer;               //Текущий таймер отслеживания
    private Vector3 roamDestination;        //Координаты узла блуждания, к которому движется бот
    private bool roamingStatus = true;      //Состояние блуждания
    private bool trackStatus = false;       //Состояние отслеживания
    private GameObject trackedPhantom;      //Объект фантома игрока

    //Состояние искусственного интеллекта
    private int currentBehaviour;
    //1-Блуждание, 2-Прямое наведение, 3-Отражающее наведение

    
    void Start() //Инициализация ИИ
    {
        agent = GetComponent<NavMeshAgent>();   //Создание агента навигации
        agent.updateRotation = false;           //Установка значений для агента
        agent.updateUpAxis = false;
        playerTransform = GameObject.Find("Character").GetComponent<Transform>();   //Запись в переменную transform компонента игрока
        currentDelay = targetDelay;     
        currentCooldown = fireCooldown; //Установка таймеров
        currentBehaviour = 1;
        targetLastPosition = playerTransform.position;  //Запись в переменную исходной позиции игрока
        roamDestination = RandomRoamingPoint();     //Установка первого узла блуждания
    }

    void LateUpdate()   //Покадровая проверка актуального состояния, перемещение бота и вызов функций стрельбы
    {
        if (Global.Game_status == false)    //Отключение ИИ в случае окончания игры
        {
            return;
        }
        timersTick();   //Отсчёт таймеров
        Vector2 playerDirection = playerTransform.position - transform.position;    //Вектор, указывающий на игрока
        RaycastHit2D hitToPlayer = Physics2D.Raycast(transform.position, playerDirection);  //Выпуск луча по направлению к игроку

        // Debug.DrawLine(transform.position,playerTransform.position,Color.magenta);

        if (hitToPlayer.collider.transform.tag == "player") //Проверка прямого попадания луча
        {
            currentBehaviour = 2;
            trackTimer = trackTime;
            //Debug.DrawLine(transform.position, playerTransform.position, Color.magenta);
        }
        else
        {
            //Debug.DrawLine(transform.position, playerTransform.position, Color.red);
            if (trackTimer > 0)
            {
                currentBehaviour = 3;
            }
            else
            {
                currentBehaviour = 1;
            }
        }

        if (currentBehaviour == 1)
        {
            trackStatus = false;
            if ((!roamingStatus) || (Vector3.Distance(transform.position,roamDestination)<1))
            {
                roamDestination = RandomRoamingPoint();
            }
            roamingStatus = true;
            moveRoaming();
        }
        if (currentBehaviour == 2)
        {
            trackStatus = false;
            roamingStatus = false;
            moveDirect();
            if (currentCooldown <= 0)
            {
                directTargeting();
            }
        }
        if (currentBehaviour == 3)
        {
            roamingStatus = false;
            if (!trackStatus)
            {
                if (trackedPhantom != null)
                {
                   Destroy(trackedPhantom);
                }
                Vector3 trajectory = (playerTransform.position - targetLastPosition) / Time.deltaTime;
                predictedPosition = playerTransform.position + (trajectory) + AimError();
                trackedPhantom = Instantiate(playerFantom, predictedPosition, Quaternion.identity);
                trackStatus = true;
            }
            
            Vector3 phantomDirection = predictedPosition - transform.position;
            RaycastHit2D phantomHit = Physics2D.Raycast(transform.position,phantomDirection);
            if (!(phantomHit.collider.tag == "phantom"))
            {
                if (currentCooldown <= 0)
                {
                    ricochetTargeting();
                }
            }
            movePredict();
        }
        targetLastPosition = playerTransform.position;
    }

    private void timersTick()//Отсчёт таймеров
    {
        if (currentDelay > 0)
        {
            currentDelay -= Time.deltaTime;
        }
        if (currentCooldown > 0)
        {
            currentCooldown -= Time.deltaTime;
        }
        if (trackTimer > 0)
        {
            trackTimer -= Time.deltaTime;
        }
    }
    private void directTargeting()//Осуществление прямого выстрела по игроку в состояние 2
    {
        /*
        Debug.Log("Direct");
        */
        Vector3 trajectory = (playerTransform.position - targetLastPosition) / Time.deltaTime;
        float timeToReach = Vector3.Distance(transform.position, playerTransform.position) / ammoProjectile.GetComponent<Bullet>().Speed;
        Vector3 targetPosition = playerTransform.position + (trajectory * timeToReach) + AimError();
        /*
        Debug.Log(playerTransform.position);
        Debug.Log(targetPosition);
        */
        Vector2 playerDirection = targetPosition - transform.position;
        float bulletRotateZ = Mathf.Atan2(playerDirection.y, playerDirection.x) * Mathf.Rad2Deg - 90;

        fire(bulletRotateZ);
    }
    private void ricochetTargeting()//Осуществление отражающего выстрела по игроку в состояние 3
    {
        if (currentDelay <= 0)
        {
            /*
            Debug.Log("Ricochet");
            */

            List<(Vector2, Vector2)> ricochetZones = getRicochetZones();

            for (int zoneNum = 0;zoneNum < ricochetZones.Count;zoneNum++)
            {

                RaycastHit2D zoneStart = Physics2D.Raycast(transform.position, ricochetZones[zoneNum].Item1);
                RaycastHit2D zoneEnd = Physics2D.Raycast(transform.position, ricochetZones[zoneNum].Item2);

                Vector2 zoneStartReflectDir = Vector2.Reflect(ricochetZones[zoneNum].Item1,zoneStart.normal);
                Vector2 zoneEndReflectDir = Vector2.Reflect(ricochetZones[zoneNum].Item2, zoneEnd.normal);

                RaycastHit2D zoneStartReflect = Physics2D.Raycast(zoneStart.point, zoneStartReflectDir);
                RaycastHit2D zoneEndReflect = Physics2D.Raycast(zoneEnd.point, zoneEndReflectDir);

                Vector2 reflectSectorStartEdge = zoneStart.point + zoneStartReflectDir * ricochetZoneDistance;
                Vector2 reflectSectorEndEdge = zoneEnd.point + zoneEndReflectDir * ricochetZoneDistance;

                /*
                Debug.DrawLine(transform.position, zoneStart.point, Color.magenta, targetDelay);
                Debug.DrawLine(transform.position, zoneEnd.point, Color.magenta, targetDelay);


                /*
                Debug.DrawLine(zoneStart.point, reflectSectorStartEdge, Color.red, targetDelay);
                Debug.DrawLine(zoneEnd.point, reflectSectorEndEdge, Color.red, targetDelay);
                */

                Vector2[] ZonePoints = new Vector2[4];
                ZonePoints[0] = zoneStart.point; ZonePoints[1] = reflectSectorStartEdge; ZonePoints[2] = reflectSectorEndEdge; ZonePoints[3] = zoneEnd.point;

                if ((localGeometry.pointInsideZone(predictedPosition, ZonePoints))||(zoneStartReflect.collider.transform.tag == "phantom") || (zoneEndReflect.collider.transform.tag == "phantom")) {

                    float radStart = (float)Math.Atan2(ricochetZones[zoneNum].Item1[1], ricochetZones[zoneNum].Item1[0]);
                    float radEnd = (float)Math.Atan2(ricochetZones[zoneNum].Item2[1], ricochetZones[zoneNum].Item2[0]);
                    float radBetween = radEnd - radStart;

                    Debug.Log(ricochetZones[zoneNum].Item1 + " Rads: "+radStart + " " + radEnd + " " + radBetween);
                    
                    /*
                    Debug.DrawLine(transform.position, zoneStart.point, Color.cyan, targetDelay);
                    Debug.DrawLine(transform.position, zoneEnd.point, Color.cyan, targetDelay);

                    Debug.DrawLine(zoneStart.point, reflectSectorStartEdge, Color.red, targetDelay);
                    Debug.DrawLine(zoneEnd.point, reflectSectorEndEdge, Color.red, targetDelay);
                    */

                    List<Vector2> hittingRays = new List<Vector2>();
                    int hits = 0;

                    for (int fireDirectionNum = 0; fireDirectionNum < ricochetZoneRays; fireDirectionNum++)
                    {
                        float rayRad = radStart + radBetween/ricochetZoneRays*fireDirectionNum;
                        float x = (float)Mathf.Cos(rayRad);
                        float y = (float)Mathf.Sin(rayRad);

                        Vector2 rayDirection = new Vector2(x, y);
                        RaycastHit2D hit = Physics2D.Raycast(transform.position, rayDirection);
                        Vector2 reflectDir = Vector2.Reflect(rayDirection, hit.normal);
                        RaycastHit2D ricochetHit = Physics2D.Raycast(hit.point, reflectDir);

                        //Debug.DrawLine(transform.position, hit.point, Color.yellow, targetDelay);
                        //Debug.DrawLine(hit.point, ricochetHit.point, Color.yellow, 0.5f);

                        if (ricochetHit.collider.transform.tag == "phantom")
                        {
                            //Debug.DrawLine(transform.position, hit.point, Color.red, 0.5f);
                            //Debug.DrawLine(hit.point, ricochetHit.point, Color.red, 0.5f);
                            hittingRays.Add(new Vector2(x, y));
                            hits++;
                        }
                    }
                    
                    if (hits != 0)
                    {

                        //Debug.Log("List size: " + hittingRays.Count);
                        Vector2 fireDirection = hittingRays[hits / 2];
                        float bulletRotateZ = Mathf.Atan2(fireDirection.y, fireDirection.x) * Mathf.Rad2Deg - 90;
                        fire(bulletRotateZ);
                        return;
                    }

                }
            }   

            currentDelay = targetDelay;
        }
    }

    private List<(Vector2, Vector2)> getRicochetZones()//Получение всех зон отражений вокруг бота
    {
        List<(Vector2, Vector2)> resList = new List<(Vector2, Vector2)>();

        float x = Mathf.Cos(0);
        float y = Mathf.Sin(0);
        Vector2 currentZoneStart = new Vector2(x, y);
        RaycastHit2D prevHit = Physics2D.Raycast(transform.position, currentZoneStart);

        Vector2 prevRayDirection = currentZoneStart;

        float normalAngle = Mathf.Atan2(prevHit.normal.y, prevHit.normal.x) * Mathf.Rad2Deg;
        float rayAngle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

        float prevCollAngle = normalAngle + rayAngle;
        if (prevCollAngle >= 360) { prevCollAngle = 360 - prevCollAngle; }

        for (int i = 1; i < ricochetZoneDetectionRays; i++)
        {
            x = Mathf.Cos(2 * Mathf.PI * i / ricochetZoneDetectionRays);
            y = Mathf.Sin(2 * Mathf.PI * i / ricochetZoneDetectionRays);
            Vector2 nextRayDirection = new Vector2(x, y);
            //Ray2D ray = new Ray2D(transform.position, newRayDirection);
            RaycastHit2D nextHit = Physics2D.Raycast(transform.position, nextRayDirection);
            //Debug.DrawLine(transform.position, nextHit.point, Color.blue, targetDelay);

            normalAngle = Mathf.Atan2(nextHit.normal.y, nextHit.normal.x) * Mathf.Rad2Deg;
            rayAngle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

            float nextCollAngle = normalAngle + rayAngle;

            nextCollAngle = nextCollAngle % 360;

            //Debug.Log("prev: "+prevCollAngle + " next: " + nextCollAngle+ " ray angle: "+rayAngle+" normalAngle: "+normalAngle);

            //Далее проводится сравнение предыдущей точки попадания с новой точкой. Это делается для определения, подходит ли новый луч для создания новой зоны отражения.
            //Для этого сравнивается расстояние между точками, их угол столкновения с поверхностью и совпадение коллайдеров.

            if ((Vector2.Distance(prevHit.point, nextHit.point) > ricochetZoneDistanceBetween)||(math.abs(prevCollAngle - nextCollAngle)>ricochetZoneAngleDifference)||(prevHit.collider!=nextHit.collider)) 
            {
                resList.Add((currentZoneStart, prevRayDirection));
                currentZoneStart = nextRayDirection;
                prevRayDirection = nextRayDirection;
                prevHit = nextHit;
                prevCollAngle = nextCollAngle;
            }
            else
            {
                prevRayDirection = nextRayDirection;
                prevHit = nextHit;
                prevCollAngle = nextCollAngle;
            }

            
        }
        return resList;
    }
        
    private void fire(float rotateZ)//Осуществление выстрела пулей ботом по указанному направлению
    {
        Instantiate(ammoProjectile, transform.position, Quaternion.Euler(0f, 0f, rotateZ));
        currentCooldown = fireCooldown;
    }

    Vector3 AimError()//Возвращает случайную точку внутри сферы, радиус которой равен значению переменной отклонения выстрела
    {
        Vector2 randomPoint = UnityEngine.Random.insideUnitSphere * aimErrorMargin;
        return randomPoint;
    }

    void moveDirect()//Направление бота к позиции игрока
    {
        agent.SetDestination(playerTransform.position);
    }

    void movePredict()//Направление бота к предполагаемой позиции игрока
    {
        agent.SetDestination(predictedPosition);
    }

    void moveRoaming()//Направление бота к случайному узлу блуждания
    {
        agent.SetDestination(roamDestination);
    }
    Vector3 RandomRoamingPoint()//Возвращает координаты случайного узла блуждания
    {
        int choice = UnityEngine.Random.Range(0, Global.roamingNodesTransforms.Length);
        Vector3 res = Global.roamingNodesTransforms[choice].position;
        //Debug.Log(res);
        return res;
    }   
   
}
static class localGeometry
{
    static public bool pointInsideZone(Vector2 point, Vector2[] p)
    {
        bool result = false;
        int j = p.Length - 1;
        for (int i = 0; i < p.Length; i++)
        {
            if ((p[i].y < point.y && p[j].y >= point.y || p[j].y < point.y && p[i].y >= point.y) &&
                 (p[i].x + (point.y - p[i].y) / (p[j].y - p[i].y) * (p[j].x - p[i].x) < point.x))
                result = !result;
            j = i;
        }
        //if (result) { Debug.Log("Point is inside"); }
        return result;
    }
}

