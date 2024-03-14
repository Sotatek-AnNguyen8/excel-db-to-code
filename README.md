# Excel-db-to-code Generator

The Excel-db-to-code Generator is a command-line tool designed to assist developers in generating models, services, controllers, and more from Excel files. It streamlines the process of translating data from Excel spreadsheets into code components, reducing manual effort and potential errors.

## Features

- Convert Excel data into various code components:
    - [x] Entities
    - [x] Dtos
    - [ ] Basic CQRS
      - [x] Get by condition
      - [x] Get by ID
      - [ ] Create
      - [ ] Update
      - [ ] Delete
      - [x] Validation
    - [ ] Controllers
- Customize templates: Define your own code generation templates to suit your project's requirements.
- Flexible configuration: Easily configure mappings between Excel columns and code properties.

## Installation

The Excel-to-Code Generator is a .NET console application and can be installed via NuGet Package Manager or by downloading the source code from the GitHub repository.

### GitHub Installation

1. Clone the GitHub repository:
```bash
git clone https://github.com/Sotatek-AnNguyen8/excel-db-to-code.git
```

1. Build the project:
```bash
dotnet build
```

## Usage

1. Duplicate `appsettings.example.json` and rename as `appsettings.json`.
2. In `appsettings.json`, modify `Source.PathToExcelFile` and `Generated.Generated` according location of input source and output.
3. Run project and enjoy.

## Contributing

Contributions to the Excel-db-to-code Generator are welcome! If you find any bugs, have feature requests, or want to contribute improvements, please submit an issue or pull request on the GitHub repository.

## License

This project is licensed under the MIT License. See the [LICENSE](https://github.com/Sotatek-AnNguyen8/excel-db-to-code/blob/master/LICENSE) file for details.

## Contact
For any inquiries or support, please contact [an.nguyen8@sotatek.com](mailto:an.nguyen8@sotatek.com).
