using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using MafiaUnity;

public class PlayerController : MonoBehaviour
{
    private PawnController characterController;
    public GameObject playerCamera;
    public GameObject playerPawn;
    private Transform cameraOrbitPoint;
    private Transform neckTransform;
    private float cameraUpAndDown = 2.01f;
    private CustomButton leftButton = new CustomButton("a");
    private CustomButton rightButton = new CustomButton("d");
    Vector3 neckStandPosition, neckCrouchPosition;
    bool isStrafing = false;
    const float CROUCH_CAMERA_DOWN = 0.8f;
    const float CAMERA_DISTANCE = 1.46f;

    // TEST ONLY
    bool test_aim = false;
    public float test_offset = -0.82f;

    public void Start()
    {
        characterController = new PawnController(playerPawn.GetComponent<ModelAnimationPlayer>(), transform);
        playerCamera.transform.position = CalculateAndUpdateCameraPosition();

        neckTransform = transform.FindDeepChild("neck");
        cameraOrbitPoint = new GameObject("cameraOrbitPoint").transform;
        cameraOrbitPoint.parent = transform;
        cameraOrbitPoint.position = neckTransform.position;
        neckCrouchPosition = neckStandPosition = cameraOrbitPoint.localPosition;
        neckCrouchPosition.y -= CROUCH_CAMERA_DOWN;
    }

    private Vector3 CalculateAndUpdateCameraPosition()
    {
        var dir = transform.forward * -CAMERA_DISTANCE;
        var pos = transform.position + dir;
        //pos += characterController.GetMovementDirection() * characterController.GetSpeed() * Time.deltaTime;
        pos.y += cameraUpAndDown;

        if (characterController.IsCrouched())
        {
            pos.y -= CROUCH_CAMERA_DOWN;

            if (cameraOrbitPoint != null)
                cameraOrbitPoint.localPosition = Vector3.Lerp(cameraOrbitPoint.localPosition, neckCrouchPosition, Time.deltaTime * 10f);
        }
        else if (cameraOrbitPoint != null)
        {
            cameraOrbitPoint.localPosition = Vector3.Lerp(cameraOrbitPoint.localPosition, neckStandPosition, Time.deltaTime * 10f);
        }

        return pos;
    }

    private void UpdateCameraMovement()
    {
        var x = Input.GetAxis("Mouse X") * Time.deltaTime * 800f;
        var y = Input.GetAxis("Mouse Y") * Time.deltaTime * 5f;

        cameraUpAndDown -= y;

        if (cameraUpAndDown < 0.9f)
            cameraUpAndDown = 0.9f;

        if (cameraUpAndDown > 3.5f)
            cameraUpAndDown = 3.5f;

        float factor = Time.deltaTime * 10f;

        if (isStrafing || characterController.IsRolling())
            factor = 1f;

        playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, CalculateAndUpdateCameraPosition(), factor);
        playerCamera.transform.LookAt(cameraOrbitPoint);
        characterController.TurnByAngle(x);
    }

    public class CustomButton
    {
        private bool reset;
        private bool firstButtonPressed;
        private string buttonName;
        private float timeOfFirstButton;

        public CustomButton(string name)
        {
            buttonName = name;
        }

        public bool Button()
        {
            return Input.GetButton(buttonName);
        }

        public bool IsDoublePressed()
        {
            bool returnVal = false;
            //TODO(DavoSK): replace with get button with simillar behaviour
            if(Input.GetKeyDown(buttonName) && firstButtonPressed) 
            {
                if(Time.time - timeOfFirstButton < 1f) 
                {
                    returnVal = true;
                } 
                reset = true;
             }
                
            if(Input.GetKeyDown(buttonName) && !firstButtonPressed) 
            {
                firstButtonPressed = true;
                timeOfFirstButton = Time.time;
            }
     
            if(reset)
            {
                firstButtonPressed = false;
                reset = false;
            }

            return returnVal;
        }
    }

    public void FixedUpdate()
    {
        if (GameAPI.instance.isPaused)
            return;

        playerCamera.transform.UpdateRenderSettings();
            
        if (characterController == null)
            return;
            
        var x = Input.GetAxisRaw("Horizontal");
        var z = Input.GetAxisRaw("Vertical");
        var isRunning = !Input.GetButton("Run");
        var isCrouching = Input.GetButton("Crouch");
        var isUsing = Input.GetButtonDown("Use");

        isStrafing = false;

        // TEST ONLY
        if (Input.GetKeyDown(KeyCode.P))
            test_aim = !test_aim;

        if (isUsing)
            UseItem();

        if(!characterController.isRolling)
        {
            if (isCrouching)
                characterController.ToggleCrouch(true);
            else
                characterController.ToggleCrouch(false);
            
            if (isRunning && !isCrouching)
                characterController.movementMode = MovementMode.Run;
            else if (!isCrouching)
                characterController.movementMode = MovementMode.Walk;

            if(leftButton.IsDoublePressed())
                characterController.RollLeft();

            if(rightButton.IsDoublePressed())
                characterController.RollRight();

            //Check even here due to code bellow :/
            if(characterController.isRolling) return;

            if (z > 0f)
            {
                characterController.MoveForward();
            }
            else if (z < 0f)
            {
                characterController.MoveBackward();
            }

            if (x > 0f)
            {
                characterController.MoveRight();
                isStrafing = true;
            }
            else if (x < 0f)
            {
                characterController.MoveLeft();
                isStrafing = true;
            }
        }

        characterController.Update();
        
        UpdateCameraMovement();

        // TEST ONLY
        if (test_aim && !characterController.IsRolling())
        {
            var look = playerCamera.transform.position;
            var back1 = playerPawn.transform.Find("base/back1").transform;
            look.y += test_offset;

            back1.LookAt(look, back1.up);
            back1.Rotate(new Vector3(0f,180f,0f), Space.Self);
            characterController.stanceMode = AnimationStanceMode.Pistol;
        }
    }

    private void UseItem()
    {
        var def = GetUsableObject();

        if (def != null)
            def.Use(playerPawn);
    }

    public IUsable GetUsableObject()
    {
        return characterController.GetUsableObject();
    }
}