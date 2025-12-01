# Cars API Basic – Final Project

This project is a fully cloud-native API built on Azure Functions, backed by Azure SQL, secured by Azure Key Vault, automated with Azure Logic Apps, and monitored through Application Insights and a custom Azure Dashboard.

It extends the midterm assignment by adding:
- Persistent database storage  
- API key authentication  
- Validation logic  
- Automated scheduled validation  
- Observability with logs, metrics, traces  


# Technologies Used

- Azure Functions
- Azure SQL Database
- Azure Key Vault
- Azure Logic App
- Application Insights
- Azure Dashboard
- C#
- GitHub


# Authentication

All endpoints require an API key passed via the header:

x-api-key: abc123

The API key is securely stored in Azure Key Vault.


# API Endpoints

POST /api/cars
Create a new car.

GET /api/cars
Retrieve all cars from SQL.

PUT /api/cars/{id}
Update an existing car by Id.

DELETE /api/cars/{id}
Delete a car by Id.

PATCH /api/cars/validate
Runs validation logic to mark older cars as classic.


# Validation Logic

A car becomes classic when:

Year < (current year - 20)

The Logic App calls this endpoint automatically and sends success/failure email notifications.


# Observability

Application Insights collects:
- HTTP request logs  
- Custom traces (ValidationTriggered, SQL updates, CRUD events)  
- SQL tracking  
- Failures  

All of these are displayed in the custom Azure Dashboard.


# Automation

A Logic App performs:
1. Retrieve API key from Key Vault via Managed Identity
2. Call `/cars/validate`
3. Check status code
4. Email if success or if failed


# Governance Features

- API Key stored in Azure Key Vault
- Managed Identity used for accessing secrets
- App Insights logging allows traceability
- Logic App run history proves automation is executed
- Azure Dashboard provides real-time monitoring


# Running Locally

1. Install Azure Function Core Tools  
2. Create a local.settings.json with:
   - SQL connection string local or cloud
   - API key
3. Run:
   
   func start
   

# Project Structure

/Cars_Api_Basic_Final
   -- Cars_API_Basic.cs
   -- Program.cs
   -- host.json
   -- README.md
   -- .gitignore
   -- Cars_Api_Basic_Final.csproj


# Screenshots

All required screenshots CRUD, Logic App, Dashboard, App Insights, SQL are included in the word document submission.


# Author
Alex McCarthy  
Sacred Heart University
