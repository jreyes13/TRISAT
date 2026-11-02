//
//  LocationViewController.h
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import <UIKit/UIKit.h>
#import <MapKit/MapKit.h>

#include "nmea.h"

@interface LocationViewController : UIViewController

@property (nonatomic,weak) IBOutlet MKMapView *mapView;

@property (weak, nonatomic) IBOutlet UILabel *clLatitude;
@property (weak, nonatomic) IBOutlet UILabel *clLongitude;
@property (weak, nonatomic) IBOutlet UILabel *clAltitude;
//@property (weak, nonatomic) IBOutlet UILabel *clHeading;
@property (weak, nonatomic) IBOutlet UILabel *clCourse;
@property (weak, nonatomic) IBOutlet UILabel *clSpeed;

@property (weak, nonatomic) IBOutlet UILabel *gpsLatitude;
@property (weak, nonatomic) IBOutlet UILabel *gpsLongitude;
@property (weak, nonatomic) IBOutlet UILabel *gpsAltitude;

@property (weak, nonatomic) IBOutlet UILabel *gpsGSTPrecisionLabel;
@property (weak, nonatomic) IBOutlet UILabel *gpsNumSatUsed;
@property (weak, nonatomic) IBOutlet UILabel *gpsNumSatTracked;
@property (weak, nonatomic) IBOutlet UILabel *gpsFixType;
@property (weak, nonatomic) IBOutlet UILabel *gpsHDOP;
@property (weak, nonatomic) IBOutlet UILabel *gpsVDOP;
@property (weak, nonatomic) IBOutlet UILabel *gpsPDOP;
@property (weak, nonatomic) IBOutlet UILabel *gpsAgeDiffCorrect;

- (void)updateGPSDataWith:(nmeaINFO)info;

@end
